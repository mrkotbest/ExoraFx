using ExoraFx.Api.Configuration;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;
using System.Net;

namespace ExoraFx.Api.Providers;

public sealed class MonobankRateProvider(IHttpClientFactory httpClientFactory, IOptions<ExchangeSettings> settings) : IRateProvider
{
    private const int UahNumericCode = 980;

    private static readonly TimeSpan MinFetchInterval = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, int> IsoCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eur"] = 978,
        ["usd"] = 840,
        ["pln"] = 985,
    };

    private readonly HttpClient _http = httpClientFactory.CreateClient("Monobank");
    private readonly ExchangeSettings _settings = settings.Value;
    private readonly Lock _cacheLock = new();
    private CacheEntry? _cache;

    public string Name => "monobank";

    public async Task<Dictionary<string, decimal>> GetRatesAsync(CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cache is { } fresh && DateTime.UtcNow - fresh.FetchedAt < MinFetchInterval)
                return Clone(fresh.Rates);
        }

        try
        {
            var resp = await _http.GetAsync("bank/currency", ct);
            resp.EnsureSuccessStatusCode();

            var entries = await resp.Content.ReadFromJsonAsync<List<MonobankCurrencyEntry>>(ct)
                ?? throw new InvalidOperationException("Monobank: null response");

            var rates = new Dictionary<string, decimal>(_settings.SupportedCurrencies.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _settings.SupportedCurrencies)
            {
                if (!IsoCodes.TryGetValue(key, out var numeric))
                    continue;

                var rate = FindRate(entries, numeric);
                if (rate is > 0)
                    rates[key] = rate.Value;
            }

            if (rates.Count == 0)
                throw new InvalidOperationException("Monobank: no supported currencies found");

            lock (_cacheLock)
            {
                _cache = new CacheEntry(rates, DateTime.UtcNow);
            }
            return Clone(rates);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            lock (_cacheLock)
            {
                if (_cache is { } stale)
                    return Clone(stale.Rates);
            }
            throw;
        }
    }

    private static Dictionary<string, decimal> Clone(Dictionary<string, decimal> source) =>
        new(source, StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(Dictionary<string, decimal> Rates, DateTime FetchedAt);

    private static decimal? FindRate(List<MonobankCurrencyEntry> entries, int numeric)
    {
        foreach (var e in entries)
        {
            if (e.CurrencyCodeA == numeric && e.CurrencyCodeB == UahNumericCode)
                return e.RateBuy ?? e.RateCross;
        }

        return null;
    }
}
