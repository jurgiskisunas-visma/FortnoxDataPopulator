namespace FortnoxDataPopulator.Services;

using Fortnox.SDK;
using Fortnox.SDK.Entities;
using Fortnox.SDK.Exceptions;

public sealed class AccountContext
{
    public required int Revenue { get; init; }
    public required int Expense { get; init; }
    public required int AccountsPayable { get; init; }
}

public static class AccountResolver
{
    public static async Task<AccountContext> ResolveAsync(FortnoxClient client)
    {
        return new AccountContext
        {
            Revenue = await EnsureAccountAsync(client, 3001, "Försäljning"),
            Expense = await EnsureAccountAsync(client, 4000, "Inköp"),
            AccountsPayable = await EnsureAccountAsync(client, 2440, "Leverantörsskulder"),
        };
    }

    private static async Task<int> EnsureAccountAsync(FortnoxClient client, int number, string description)
    {
        try
        {
            await client.AccountConnector.CreateAsync(new Account
            {
                Number = number,
                Description = description,
                Active = true,
            });
            Console.WriteLine($"  Created account {number} ({description}).");
        }
        catch (FortnoxApiException ex) when (IsAlreadyExists(ex))
        {
            // Account already exists — nothing to do, just use it.
        }
        catch (FortnoxApiException ex)
        {
            Console.WriteLine(
                $"  Could not create account {number}: {ex.ErrorInfo?.Message ?? ex.Message} " +
                $"(code {ex.ErrorInfo?.Code ?? "?"}). Invoice creation may fail.");
        }

        return number;
    }

    private static bool IsAlreadyExists(FortnoxApiException ex)
    {
        var message = ex.ErrorInfo?.Message ?? ex.Message ?? string.Empty;
        return message.Contains("existerar redan", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
