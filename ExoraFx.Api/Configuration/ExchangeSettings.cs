namespace ExoraFx.Api.Configuration;

public sealed class ExchangeSettings
{
    public const string SectionName = "Exchange";

    public decimal MarginPercent { get; init; } = 7.7m;
    public int CacheTtlSeconds { get; init; } = 300;
    public int RefreshIntervalSeconds { get; init; } = 240;
    public string DefaultBank { get; init; } = "monobank";
    public List<string> SupportedCurrencies { get; init; } = [];
}
