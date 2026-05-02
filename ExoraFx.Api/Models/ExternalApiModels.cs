using System.Text.Json.Serialization;

namespace ExoraFx.Api.Models;

public sealed class MonobankCurrencyEntry
{
    public int CurrencyCodeA { get; set; }
    public int CurrencyCodeB { get; set; }
    public decimal? RateBuy { get; set; }
    public decimal? RateCross { get; set; }
}

public sealed class NbuRateEntry
{
    [JsonPropertyName("rate")] public decimal Rate { get; set; }
}

public sealed class PrivatBankRateEntry
{
    [JsonPropertyName("ccy")] public string Ccy { get; set; } = "";
    [JsonPropertyName("buy")] public string Buy { get; set; } = "";
}
