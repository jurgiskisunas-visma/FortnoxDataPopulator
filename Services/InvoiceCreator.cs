namespace FortnoxDataPopulator.Services;

using System.Diagnostics;
using System.Globalization;
using Fortnox.SDK;
using Fortnox.SDK.Entities;
using Fortnox.SDK.Exceptions;

public readonly record struct BatchResult(int Succeeded, int Total, TimeSpan Elapsed);

public static class InvoiceCreator
{
    private static readonly Random Rng = new Random();

    public static async Task<BatchResult> AddCustomerInvoicesAsync(FortnoxClient client, int count)
    {
        if (count <= 0)
        {
            return new BatchResult(0, 0, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var fy = await FinancialYearInfo.GetUsableAsync(client);
        var accounts = await AccountResolver.ResolveAsync(client);
        await VoucherSeriesResolver.EnsureDefaultsAsync(client);
        var customerName = NameGenerator.CustomerName();
        Console.WriteLine();
        Console.WriteLine($"--- Creating {count} customer invoice(s) for \"{customerName}\" ---");
        Console.WriteLine($"  Using revenue account {accounts.Revenue}.");
        WarnIfNoFinancialYear(fy);

        Customer customer;
        try
        {
            customer = await CreateCustomerAsync(client, customerName);
        }
        catch (FortnoxApiException ex)
        {
            LogFortnoxError("Failed to create customer", ex);
            stopwatch.Stop();
            return new BatchResult(0, count, stopwatch.Elapsed);
        }

        Console.WriteLine($"Customer created: #{customer.CustomerNumber} — {customer.Name}");

        var ok = 0;
        var posted = 0;
        for (var i = 0; i < count; i++)
        {
            var invoice = BuildCustomerInvoice(customer.CustomerNumber, i, count, accounts, fy);
            try
            {
                var created = await client.InvoiceConnector.CreateAsync(invoice);
                ok++;

                var postStatus = await TryBookkeepCustomerAsync(client, created.DocumentNumber);
                if (postStatus.Posted)
                {
                    posted++;
                }

                Console.WriteLine(
                    $"[{Now()}][{i + 1}/{count}] invoice #{created.DocumentNumber} - " +
                    $"{created.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"} {created.Currency} " +
                    $"({postStatus.Label})");
            }
            catch (FortnoxApiException ex)
            {
                LogFortnoxError($"[{Now()}][{i + 1}/{count}] failed", ex);
            }
        }

        stopwatch.Stop();
        Console.WriteLine(
            $"Done: {ok}/{count} customer invoices created, {posted}/{ok} posted, in {FormatElapsed(stopwatch.Elapsed)}.");
        return new BatchResult(ok, count, stopwatch.Elapsed);
    }

    public static async Task<BatchResult> AddSupplierInvoicesAsync(FortnoxClient client, int count)
    {
        if (count <= 0)
        {
            return new BatchResult(0, 0, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var fy = await FinancialYearInfo.GetUsableAsync(client);
        var accounts = await AccountResolver.ResolveAsync(client);
        await VoucherSeriesResolver.EnsureDefaultsAsync(client);
        var supplierName = NameGenerator.CompanyName();
        Console.WriteLine();
        Console.WriteLine($"--- Creating {count} supplier invoice(s) from \"{supplierName}\" ---");
        Console.WriteLine($"  Using expense account {accounts.Expense} (Fortnox auto-balances the AP side).");
        WarnIfNoFinancialYear(fy);

        Supplier supplier;
        try
        {
            supplier = await CreateSupplierAsync(client, supplierName);
        }
        catch (FortnoxApiException ex)
        {
            LogFortnoxError("Failed to create supplier", ex);
            stopwatch.Stop();
            return new BatchResult(0, count, stopwatch.Elapsed);
        }

        Console.WriteLine($"Supplier created: #{supplier.SupplierNumber} — {supplier.Name}");

        var ok = 0;
        var posted = 0;
        for (var i = 0; i < count; i++)
        {
            var invoice = BuildSupplierInvoice(supplier.SupplierNumber, i, count, accounts, fy);
            try
            {
                var created = await client.SupplierInvoiceConnector.CreateAsync(invoice);
                ok++;

                var postStatus = await TryBookkeepSupplierAsync(client, created.GivenNumber);
                if (postStatus.Posted)
                {
                    posted++;
                }

                Console.WriteLine(
                    $"[{Now()}][{i + 1}/{count}] invoice #{created.GivenNumber} - " +
                    $"{created.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"} {created.Currency} " +
                    $"({postStatus.Label})");
            }
            catch (FortnoxApiException ex)
            {
                LogFortnoxError($"[{Now()}][{i + 1}/{count}] failed", ex);
            }
        }

        stopwatch.Stop();
        Console.WriteLine(
            $"Done: {ok}/{count} supplier invoices created, {posted}/{ok} posted, in {FormatElapsed(stopwatch.Elapsed)}.");
        return new BatchResult(ok, count, stopwatch.Elapsed);
    }

    private static async Task<(bool Posted, string Label)> TryBookkeepCustomerAsync(FortnoxClient client, long? documentNumber)
    {
        if (!documentNumber.HasValue)
        {
            return (false, "NOT posted: no document number");
        }

        try
        {
            await client.InvoiceConnector.BookkeepAsync(documentNumber.Value);
            return (true, "posted");
        }
        catch (FortnoxApiException ex)
        {
            return (false, $"NOT posted: {ex.ErrorInfo?.Message ?? ex.Message}");
        }
    }

    private static async Task<(bool Posted, string Label)> TryBookkeepSupplierAsync(FortnoxClient client, long? givenNumber)
    {
        if (!givenNumber.HasValue)
        {
            return (false, "NOT posted: no document number");
        }

        try
        {
            await client.SupplierInvoiceConnector.BookkeepAsync(givenNumber.Value);
            return (true, "posted");
        }
        catch (FortnoxApiException ex)
        {
            return (false, $"NOT posted: {ex.ErrorInfo?.Message ?? ex.Message}");
        }
    }

    private static void WarnIfNoFinancialYear(FinancialYearInfo? fy)
    {
        if (fy == null)
        {
            Console.WriteLine(
                "  WARNING: No financial years configured in this company. Invoice posting will fail. " +
                "Configure a financial year in Fortnox settings first.");
            return;
        }

        var suffix = fy.CoversToday
            ? " (contains today)"
            : $" (today is outside this year — invoices will be dated {fy.ToDate:yyyy-MM-dd})";
        Console.WriteLine($"  Financial year: {fy.FromDate:yyyy-MM-dd} to {fy.ToDate:yyyy-MM-dd}{suffix}.");
    }

    private static DateTime PickInvoiceDate(int index, FinancialYearInfo? fy)
    {
        // Anchor at today, but never outside the current financial year.
        var today = DateTime.Today;
        var anchor = fy is null ? today : fy.Clamp(today);

        // Spread across up to 30 days back, but keep the result inside the FY.
        var candidate = anchor.AddDays(-(index % 30));
        return fy?.Clamp(candidate) ?? candidate;
    }

    private static string Now() => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:0} ms";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return $"{elapsed.TotalSeconds:0.0} s";
        }

        return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
    }

    private static async Task<Customer> CreateCustomerAsync(FortnoxClient client, string name)
    {
        var tag = NameGenerator.ShortTag();
        var customer = new Customer
        {
            Name = name,
            Type = CustomerType.Company,
            CountryCode = "SE",
            Currency = "SEK",
            Address1 = $"{1 + Rng.Next(200)} Klarabergsgatan",
            ZipCode = "11122",
            City = "Stockholm",
            Email = $"hello+{tag}@example.com",
        };

        return await client.CustomerConnector.CreateAsync(customer);
    }

    private static async Task<Supplier> CreateSupplierAsync(FortnoxClient client, string name)
    {
        var tag = NameGenerator.ShortTag();
        var supplier = new Supplier
        {
            Name = name,
            CountryCode = "SE",
            Currency = "SEK",
            Address1 = $"{1 + Rng.Next(200)} Sveavägen",
            ZipCode = "11122",
            City = "Stockholm",
            Email = $"billing+{tag}@example.com",
        };

        return await client.SupplierConnector.CreateAsync(supplier);
    }

    private static Invoice BuildCustomerInvoice(string customerNumber, int index, int total, AccountContext accounts, FinancialYearInfo? fy)
    {
        var invoiceDate = PickInvoiceDate(index, fy);
        var dueDate = invoiceDate.AddDays(30);

        var rows = new List<InvoiceRow>();
        var rowCount = 1 + (index % 3); // 1, 2 or 3 rows
        for (var r = 0; r < rowCount; r++)
        {
            rows.Add(new InvoiceRow
            {
                AccountNumber = accounts.Revenue,
                Description = NameGenerator.CustomerRowDescription(index * 10 + r),
                DeliveredQuantity = 1 + ((index + r) % 5),
                Price = 100m + (((index * 7) + (r * 13)) % 20) * 50m,
                VAT = 25,
            });
        }

        return new Invoice
        {
            CustomerNumber = customerNumber,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Comments = NameGenerator.InvoiceComment(index),
            Remarks = $"Invoice {index + 1} of {total}",
            InvoiceRows = rows,
        };
    }

    private static SupplierInvoice BuildSupplierInvoice(string supplierNumber, int index, int total, AccountContext accounts, FinancialYearInfo? fy)
    {
        var invoiceDate = PickInvoiceDate(index, fy);
        var dueDate = invoiceDate.AddDays(30);

        var amount = 500m + (((index * 11) + 3) % 40) * 25m;

        // Provide only the expense (debit) row. Fortnox automatically adds the balancing
        // accounts-payable (credit) row based on Total and the supplier's default AP account.
        // Adding a manual AP row here duplicates it and breaks the balance.
        return new SupplierInvoice
        {
            SupplierNumber = supplierNumber,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            Total = amount,
            Currency = "SEK",
            Comments = $"{NameGenerator.SupplierRowDescription(index)} - invoice {index + 1} of {total}",
            SupplierInvoiceRows = new List<SupplierInvoiceRow>
            {
                new SupplierInvoiceRow { Account = accounts.Expense, Debit = amount, Credit = 0 },
            },
        };
    }

    private static void LogFortnoxError(string prefix, FortnoxApiException ex)
    {
        Console.WriteLine($"{prefix}: {ex.Message}");
        if (ex.ErrorInfo != null)
        {
            Console.WriteLine($"  Code: {ex.ErrorInfo.Code} — {ex.ErrorInfo.Message}");
        }
    }
}
