namespace FortnoxDataPopulator.Services;

using Fortnox.SDK;
using Fortnox.SDK.Entities;
using Fortnox.SDK.Exceptions;
using Fortnox.SDK.Search;

public sealed class FinancialYearInfo
{
    public required long Id { get; init; }
    public required DateTime FromDate { get; init; }
    public required DateTime ToDate { get; init; }
    public required bool CoversToday { get; init; }

    public static async Task<FinancialYearInfo?> GetUsableAsync(FortnoxClient client)
    {
        try
        {
            // 1. Prefer the FY that covers today.
            var covering = await FindCoveringTodayAsync(client);
            if (covering != null)
            {
                return ToInfo(covering, coversToday: true);
            }

            // 2. Fall back to the most recent FY the company has.
            var all = await client.FinancialYearConnector.FindAsync(new FinancialYearSearch());
            var latest = all.Entities
                .Where(y => y.FromDate.HasValue && y.ToDate.HasValue && y.Id.HasValue)
                .OrderByDescending(y => y.ToDate!.Value)
                .FirstOrDefault();

            return latest == null ? null : ToInfo(latest, coversToday: false);
        }
        catch (FortnoxApiException)
        {
            return null;
        }
    }

    public DateTime Clamp(DateTime candidate)
    {
        if (candidate < this.FromDate)
        {
            return this.FromDate;
        }

        if (candidate > this.ToDate)
        {
            return this.ToDate;
        }

        return candidate;
    }

    private static async Task<FinancialYearSubset?> FindCoveringTodayAsync(FortnoxClient client)
    {
        var result = await client.FinancialYearConnector.FindAsync(new FinancialYearSearch
        {
            Date = DateTime.Today,
        });

        var candidate = result.Entities.FirstOrDefault();
        if (candidate?.FromDate is null || candidate.ToDate is null || candidate.Id is null)
        {
            return null;
        }

        // Fortnox sometimes returns the closest FY even if Date isn't inside it — verify.
        var today = DateTime.Today;
        if (candidate.FromDate.Value <= today && today <= candidate.ToDate.Value)
        {
            return candidate;
        }

        return null;
    }

    private static FinancialYearInfo ToInfo(FinancialYearSubset subset, bool coversToday) => new()
    {
        Id = subset.Id!.Value,
        FromDate = subset.FromDate!.Value,
        ToDate = subset.ToDate!.Value,
        CoversToday = coversToday,
    };
}
