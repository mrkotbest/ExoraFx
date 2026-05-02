namespace ExoraFx.Api.Models;

public record CachedBankRate(decimal Rate, DateTime FetchedAt);

public sealed class ConvertResult
{
    public required string From { get; init; }
    public required decimal FromAmount { get; init; }
    public required string To { get; init; }
    public required decimal ToAmount { get; init; }
    public required decimal EffectiveRate { get; init; }
    public required decimal OfficialRate { get; init; }
    public required string Bank { get; init; }
    public required decimal MarginPercent { get; init; }
    public required decimal ProfitUah { get; init; }
    public decimal? ProfitEur { get; init; }
    public required int RateAgeSec { get; init; }
    public required bool IsStale { get; init; }
    public required string CalculatedAt { get; init; }
}

public sealed class HealthResponse
{
    public required string Status { get; init; }
    public required string Uptime { get; init; }
    public string? LastRefreshAt { get; init; }
    public required List<BankStatus> Banks { get; init; }
}

public sealed class BankStatus
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required int RateAgeSec { get; init; }
    public string? LastError { get; init; }
}
