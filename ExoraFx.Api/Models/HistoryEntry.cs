namespace ExoraFx.Api.Models;

public sealed record HistoryEntry(
    long Id,
    long UserId,
    string? UserName,
    string? UserRole,
    string FromCurrency,
    string ToCurrency,
    decimal FromAmount,
    decimal ToAmount,
    string Bank,
    decimal MarginPercent,
    decimal ProfitUah,
    decimal? ProfitEur,
    DateTime CreatedAtUtc,
    string State);

public static class HistoryState
{
    public const string Draft = "draft";
    public const string Done = "done";
}

public sealed record HistoryStats(
    int TotalCount,
    int DoneCount,
    int DraftCount,
    DateTime? FirstAtUtc,
    DateTime? LastAtUtc,
    IReadOnlyDictionary<string, decimal> ReceivedByCurrency,
    decimal PaidUah,
    decimal ProfitUah,
    decimal ProfitEur,
    decimal AverageMarginPercent,
    string? TopBank,
    int TopBankCount,
    string? TopCurrency,
    decimal MaxTradeUah,
    string? MaxTradeFromCurrency,
    decimal MaxTradeFromAmount);

public static class StatsPeriod
{
    public const string Today = "td";
    public const string Week = "7d";
    public const string Month = "30d";
    public const string All = "all";

    public static (DateTime? Since, DateTime? Until) Bounds(string period)
    {
        var nowUtc = DateTime.UtcNow;
        return period switch
        {
            Today => (nowUtc.Date, null),
            Week => (nowUtc.Date.AddDays(-6), null),
            Month => (nowUtc.Date.AddDays(-29), null),
            _ => (null, null),
        };
    }

    public static bool IsValid(string period) => period is Today or Week or Month or All;
}
