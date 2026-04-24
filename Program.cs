namespace FortnoxConsoleApp;

using System.Globalization;
using FortnoxConsoleApp.Services;
using Microsoft.Extensions.Configuration;

public static class Program
{
    public static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Fortnox Console App");
        Console.WriteLine("===================");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        var authService = new FortnoxAuthService(config);

        Fortnox.SDK.FortnoxClient client;
        try
        {
            client = await authService.AuthenticateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication failed: {ex.Message}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Authenticated. Ready to create invoices.");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Choose an option:");
            Console.WriteLine("  1) Add customer invoice(s)");
            Console.WriteLine("  2) Add supplier invoice(s)");
            Console.WriteLine("  3) Add both (customer + supplier)");
            Console.WriteLine("  r) Re-authenticate (clears cached token)");
            Console.WriteLine("  q) Quit");
            Console.Write("> ");

            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            try
            {
                switch (input)
                {
                    case "1":
                    {
                        var n = PromptInt("How many customer invoices", 1);
                        await InvoiceCreator.AddCustomerInvoicesAsync(client, n);
                        break;
                    }

                    case "2":
                    {
                        var n = PromptInt("How many supplier invoices", 1);
                        await InvoiceCreator.AddSupplierInvoicesAsync(client, n);
                        break;
                    }

                    case "3":
                    {
                        var n = PromptInt("How many of each (customer + supplier)", 1);
                        var customerResult = await InvoiceCreator.AddCustomerInvoicesAsync(client, n);
                        var supplierResult = await InvoiceCreator.AddSupplierInvoicesAsync(client, n);
                        var total = customerResult.Elapsed + supplierResult.Elapsed;
                        Console.WriteLine();
                        Console.WriteLine(
                            $"Grand total: {customerResult.Succeeded} customer + {supplierResult.Succeeded} supplier " +
                            $"({customerResult.Succeeded + supplierResult.Succeeded} invoices) " +
                            $"in {InvoiceCreator.FormatElapsed(total)} " +
                            $"(customer {InvoiceCreator.FormatElapsed(customerResult.Elapsed)}, " +
                            $"supplier {InvoiceCreator.FormatElapsed(supplierResult.Elapsed)}).");
                        break;
                    }

                    case "r":
                    case "reauth":
                        authService.ResetTokens();
                        Console.WriteLine("Cached token cleared. Re-running authorization...");
                        client = await authService.AuthenticateAsync();
                        Console.WriteLine("Re-authenticated.");
                        break;

                    case "q":
                    case "quit":
                    case "exit":
                        Console.WriteLine("Bye.");
                        return;

                    default:
                        Console.WriteLine("Unknown option. Enter 1, 2, 3, or q.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private static int PromptInt(string label, int defaultValue)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            if (input.Length == 0)
            {
                return defaultValue;
            }

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            {
                return n;
            }

            Console.WriteLine("  Enter a positive integer.");
        }
    }
}
