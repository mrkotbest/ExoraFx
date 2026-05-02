using ExoraFx.Api.Configuration;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace ExoraFx.Api.Providers;

public sealed class PrivatBankProvider(IHttpClientFactory httpClientFactory, IOptions<ExchangeSettings> settings) : IRateProvider
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { "eur", "usd" };

    private readonly HttpClient _http = httpClientFactory.CreateClient("PrivatBank");
    private readonly ExchangeSettings _settings = settings.Value;

    public string Name => "privatbank";

    public async Task<Dictionary<string, decimal>> GetRatesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("p24api/pubinfo?exchange&coursid=5", ct);
        resp.EnsureSuccessStatusCode();

        var entries = await resp.Content.ReadFromJsonAsync<List<PrivatBankRateEntry>>(ct)
            ?? throw new InvalidOperationException("PrivatBank: null response");

        var rates = new Dictionary<string, decimal>(_settings.SupportedCurrencies.Count);
        foreach (var key in _settings.SupportedCurrencies)
        {
            if (!Supported.Contains(key))
                continue;

            if (TryParseBuy(entries, key, out var rate))
                rates[key] = rate;
        }

        return rates.Count > 0
            ? rates
            : throw new InvalidOperationException("PrivatBank: no supported currencies found");
    }

    private static bool TryParseBuy(List<PrivatBankRateEntry> entries, string key, out decimal rate)
    {
        foreach (var e in entries)
        {
            if (!e.Ccy.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            if (decimal.TryParse(e.Buy, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                rate = parsed;
                return true;
            }
        }

        rate = 0;
        return false;
    }
}
