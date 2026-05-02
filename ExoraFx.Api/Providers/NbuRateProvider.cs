using ExoraFx.Api.Configuration;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Providers;

public sealed class NbuRateProvider(IHttpClientFactory httpClientFactory, IOptions<ExchangeSettings> settings, ILogger<NbuRateProvider> logger) : IRateProvider
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("Nbu");
    private readonly ExchangeSettings _settings = settings.Value;

    public string Name => "nbu";

    public async Task<Dictionary<string, decimal>> GetRatesAsync(CancellationToken ct = default)
    {
        var supported = _settings.SupportedCurrencies;
        var tasks = new Task<(string Key, decimal Rate)?>[supported.Count];
        for (var i = 0; i < supported.Count; i++)
        {
            var key = supported[i];
            tasks[i] = FetchOne(key.ToUpperInvariant(), key, ct);
        }

        var results = await Task.WhenAll(tasks);

        var rates = new Dictionary<string, decimal>(results.Length);
        foreach (var r in results)
        {
            if (r is { } pair)
                rates[pair.Key] = pair.Rate;
        }

        return rates.Count > 0
            ? rates
            : throw new InvalidOperationException("NBU: no rates fetched");
    }

    private async Task<(string Key, decimal Rate)?> FetchOne(string valcode, string key, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync($"NBUStatService/v1/statdirectory/exchange?valcode={valcode}&json", ct);
            resp.EnsureSuccessStatusCode();

            var entries = await resp.Content.ReadFromJsonAsync<List<NbuRateEntry>>(ct);
            if (entries is { Count: > 0 } && entries[0].Rate > 0)
                return (key, entries[0].Rate);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NBU: failed {Valcode}", valcode);
        }

        return null;
    }
}
