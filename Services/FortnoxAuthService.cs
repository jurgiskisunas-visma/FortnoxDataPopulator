namespace FortnoxConsoleApp.Services;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fortnox.SDK;
using Fortnox.SDK.Auth;
using Fortnox.SDK.Authorization;
using Microsoft.Extensions.Configuration;

public sealed class FortnoxAuthService
{
    private readonly string clientId;
    private readonly string clientSecret;
    private readonly string redirectUri;
    private readonly string tokenFilePath;

    public FortnoxAuthService(IConfiguration config)
    {
        this.clientId = config["Fortnox:ClientId"]
            ?? throw new InvalidOperationException("Fortnox:ClientId is not configured.");
        this.clientSecret = config["Fortnox:ClientSecret"]
            ?? throw new InvalidOperationException("Fortnox:ClientSecret is not configured.");
        this.redirectUri = config["Fortnox:RedirectUri"]
            ?? throw new InvalidOperationException("Fortnox:RedirectUri is not configured.");

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortnoxConsoleApp");
        Directory.CreateDirectory(appDataDir);
        this.tokenFilePath = Path.Combine(appDataDir, "tokens.json");
    }

    public void ResetTokens()
    {
        if (File.Exists(this.tokenFilePath))
        {
            File.Delete(this.tokenFilePath);
        }
    }

    public async Task<FortnoxClient> AuthenticateAsync()
    {
        var stored = LoadTokens(this.tokenFilePath);

        if (stored != null && stored.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2))
        {
            Console.WriteLine($"Using cached access token (expires at {stored.ExpiresAtUtc:u}).");
            return BuildClient(stored.AccessToken);
        }

        if (stored != null && !string.IsNullOrEmpty(stored.RefreshToken))
        {
            try
            {
                Console.WriteLine("Access token missing or near expiry — refreshing...");
                var refreshed = await this.RefreshAsync(stored.RefreshToken);
                SaveTokens(this.tokenFilePath, refreshed);
                return BuildClient(refreshed.AccessToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Refresh failed ({ex.Message}). Will re-authorize via browser.");
            }
        }

        var fresh = await this.RunBrowserAuthorizationAsync();
        SaveTokens(this.tokenFilePath, fresh);
        return BuildClient(fresh.AccessToken);
    }

    private async Task<StoredTokens> RefreshAsync(string refreshToken)
    {
        var authClient = new FortnoxAuthClient();
        var tokenInfo = await authClient.StandardAuthWorkflow.RefreshTokenAsync(
            refreshToken, this.clientId, this.clientSecret);

        return new StoredTokens
        {
            AccessToken = tokenInfo.AccessToken,
            RefreshToken = tokenInfo.RefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenInfo.ExpiresIn - 60),
        };
    }

    private async Task<StoredTokens> RunBrowserAuthorizationAsync()
    {
        var scopes = new List<Scope>
        {
            Scope.Bookkeeping,
            Scope.CostCenter,
            Scope.Project,
            Scope.CompanyInformation,
            Scope.Customer,
            Scope.Supplier,
            Scope.Article,
            Scope.Invoice,
            Scope.Payment,
            Scope.SupplierInvoice,
            Scope.Settings,
        };

        var state = Guid.NewGuid().ToString("N");
        var authClient = new FortnoxAuthClient();
        var authUri = authClient.StandardAuthWorkflow
            .BuildAuthUri(this.clientId, scopes, state, this.redirectUri)
            .ToString();

        var redirect = new Uri(this.redirectUri);

        var listener = new TcpListener(IPAddress.Loopback, redirect.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Could not bind TCP listener on port {redirect.Port}. " +
                $"Ensure the port is free and matches the RedirectUri registered in your Fortnox app. ({ex.Message})",
                ex);
        }

        Console.WriteLine();
        Console.WriteLine("Opening browser to authorize with Fortnox...");
        Console.WriteLine($"If the browser doesn't open, visit:\n  {authUri}");
        Console.WriteLine();

        OpenBrowser(authUri);

        string code;
        string? returnedState;
        try
        {
            (code, returnedState) = await WaitForCallbackAsync(listener, redirect.AbsolutePath);
        }
        finally
        {
            listener.Stop();
        }

        if (returnedState != state)
        {
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF. Aborting.");
        }

        Console.WriteLine("Authorization code received — exchanging for tokens...");

        var tokenInfo = await authClient.StandardAuthWorkflow.GetTokenAsync(
            code, this.clientId, this.clientSecret, this.redirectUri);

        return new StoredTokens
        {
            AccessToken = tokenInfo.AccessToken,
            RefreshToken = tokenInfo.RefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenInfo.ExpiresIn - 60),
        };
    }

    private static async Task<(string Code, string? State)> WaitForCallbackAsync(
        TcpListener listener, string expectedPath)
    {
        while (true)
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            using var stream = tcpClient.GetStream();

            var requestLine = await ReadLineAsync(stream);
            if (string.IsNullOrEmpty(requestLine))
            {
                continue;
            }

            while (!string.IsNullOrEmpty(await ReadLineAsync(stream)))
            {
                // Drain headers until the blank line.
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
            {
                await WriteResponseAsync(stream, 400, "Bad Request", "Bad request.");
                continue;
            }

            var target = parts[1];
            var targetUri = new Uri("http://localhost" + target);

            if (!string.Equals(targetUri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, 404, "Not Found", "Not found.");
                continue;
            }

            var query = ParseQuery(targetUri.Query);
            query.TryGetValue("code", out var code);
            query.TryGetValue("state", out var state);
            query.TryGetValue("error", out var error);

            var body = error != null
                ? $"<html><body><h1>Authorization failed</h1><p>{WebUtility.HtmlEncode(error)}</p></body></html>"
                : "<html><body><h1>Authorization complete</h1><p>You can close this tab and return to the console.</p></body></html>";
            await WriteResponseAsync(stream, 200, "OK", body);

            if (error != null)
            {
                throw new InvalidOperationException($"Authorization denied: {error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("Callback did not include authorization code.");
            }

            return (code, state);
        }
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(128);
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1));
            if (read == 0)
            {
                break;
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                buffer.Add(one[0]);
            }
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string reason, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers =
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[WebUtility.UrlDecode(pair)] = string.Empty;
            }
            else
            {
                var key = WebUtility.UrlDecode(pair[..eq]);
                var value = WebUtility.UrlDecode(pair[(eq + 1)..]);
                result[key] = value;
            }
        }

        return result;
    }

    private static FortnoxClient BuildClient(string accessToken)
    {
        return new FortnoxClient(new StandardAuth(accessToken));
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // If auto-open fails, user can copy the URL from stdout.
        }
    }

    private static StoredTokens? LoadTokens(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoredTokens>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveTokens(string path, StoredTokens tokens)
    {
        var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private sealed class StoredTokens
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresAtUtc")]
        public DateTime ExpiresAtUtc { get; set; }
    }
}
