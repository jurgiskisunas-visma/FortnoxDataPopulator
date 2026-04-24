namespace FortnoxConsoleApp.Services;

using Fortnox.SDK;
using Fortnox.SDK.Entities;
using Fortnox.SDK.Exceptions;
using Fortnox.SDK.Search;

public static class VoucherSeriesResolver
{
    // Fortnox predefined voucher series type names (the Name property on PredefinedVoucherSeries).
    private const string SupplierInvoiceType = "SupplierInvoice";
    private const string CustomerInvoiceType = "Invoice";

    // Fortnox's conventional series codes.
    //   F = Leverantörsfakturor (supplier invoices)
    //   C = Kundfakturor (customer invoices)
    //   A = Generic / Huvudbok — common fallback
    private static readonly string[] SupplierPreferred = { "F", "A" };
    private static readonly string[] CustomerPreferred = { "C", "A" };

    public static async Task EnsureDefaultsAsync(FortnoxClient client)
    {
        var availableCodes = await GetAvailableSeriesCodesAsync(client);
        if (availableCodes.Count == 0)
        {
            Console.WriteLine("  WARNING: No voucher series found. Posting will fail. Configure one in Fortnox.");
            return;
        }

        Console.WriteLine($"  Available voucher series: {string.Join(", ", availableCodes)}");

        var preds = await GetPredefinedAsync(client);

        var supplier = preds.FirstOrDefault(p => p.Name == SupplierInvoiceType);
        var customer = preds.FirstOrDefault(p => p.Name == CustomerInvoiceType);

        await EnsurePredefinedAsync(client, supplier, SupplierInvoiceType, availableCodes, SupplierPreferred);
        await EnsurePredefinedAsync(client, customer, CustomerInvoiceType, availableCodes, CustomerPreferred);
    }

    private static async Task<List<string>> GetAvailableSeriesCodesAsync(FortnoxClient client)
    {
        try
        {
            var result = await client.VoucherSeriesConnector.FindAsync(new VoucherSeriesSearch());
            return result.Entities
                .Where(s => !string.IsNullOrEmpty(s.Code))
                .Select(s => s.Code!)
                .Distinct()
                .ToList();
        }
        catch (FortnoxApiException)
        {
            return new List<string>();
        }
    }

    private static async Task<List<PredefinedVoucherSeries>> GetPredefinedAsync(FortnoxClient client)
    {
        try
        {
            var result = await client.PredefinedVoucherSeriesConnector.FindAsync(new PredefinedVoucherSeriesSearch());
            return result.Entities.ToList();
        }
        catch (FortnoxApiException)
        {
            return new List<PredefinedVoucherSeries>();
        }
    }

    private static async Task EnsurePredefinedAsync(
        FortnoxClient client,
        PredefinedVoucherSeries? current,
        string typeName,
        List<string> availableCodes,
        string[] preferredCodes)
    {
        // Already has a value that exists as a real voucher series — leave it alone.
        if (current != null
            && !string.IsNullOrEmpty(current.VoucherSeries)
            && availableCodes.Contains(current.VoucherSeries))
        {
            Console.WriteLine($"  {typeName}: using existing default '{current.VoucherSeries}'.");
            return;
        }

        var chosen = preferredCodes.FirstOrDefault(availableCodes.Contains)
                     ?? availableCodes.First();

        if (current == null)
        {
            // Predefined record doesn't exist; the PUT endpoint is the only option and it needs the
            // entity coming from a Get/Find response so Name is server-populated. Fetch individually.
            try
            {
                current = await client.PredefinedVoucherSeriesConnector.GetAsync(typeName);
            }
            catch (FortnoxApiException ex)
            {
                Console.WriteLine($"  {typeName}: could not load predefined record ({ex.ErrorInfo?.Message ?? ex.Message}).");
                return;
            }

            if (current == null)
            {
                Console.WriteLine($"  {typeName}: predefined record not available in this company.");
                return;
            }
        }

        current.VoucherSeries = chosen;

        try
        {
            await client.PredefinedVoucherSeriesConnector.UpdateAsync(current);
            Console.WriteLine($"  {typeName}: default set to '{chosen}'.");
        }
        catch (FortnoxApiException ex)
        {
            Console.WriteLine($"  {typeName}: could not set default to '{chosen}': {ex.ErrorInfo?.Message ?? ex.Message}");
        }
    }
}
