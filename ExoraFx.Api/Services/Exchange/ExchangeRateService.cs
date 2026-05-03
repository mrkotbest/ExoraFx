using ExoraFx.Api.Helpers;
using ExoraFx.Api.Models;
using ExoraFx.Api.Providers;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace ExoraFx.Api.Services.Exchange;

public sealed class ExchangeRateService(IEnumerable<IRateProvider> providers, IMemoryCache cache, ILogger<ExchangeRateService> logger) : IExchangeRateService
{
    private const string CacheKey = "AllBankRates";

    private readonly IRateProvider[] _providers = [.. providers];
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, string?> _lastErrors = new();
    private DateTime? _lastRefreshAtUtc;

    public CachedBankRate? GetRate(string bank, string currency) =>
        RawCache().GetValueOrDefault(BuildKey(bank, currency));

    public Dictionary<string, CachedBankRate> GetAllRates(string currency)
    {
        var raw = RawCache();
        var suffix = $":{currency.ToLowerInvariant()}";
        var result = new Dictionary<string, CachedBankRate>();

        foreach (var (key, value) in raw)
        {
            if (!key.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var sep = key.IndexOf(':');
            result[key[..sep]] = value;
        }

        return result;
    }

    public string? FindBestBank(string fromCurrency, string toCurrency)
    {
        var fromL = fromCurrency.ToLowerInvariant();
        var toL = toCurrency.ToLowerInvariant();
        if (fromL == toL || (fromL != CurrencyHelper.Uah && toL != CurrencyHelper.Uah))
            return null;

        var foreign = fromL == CurrencyHelper.Uah ? toL : fromL;
        var allRates = GetAllRates(foreign);
        return allRates.Count == 0 ? null : allRates.MinBy(kv => kv.Value.Rate).Key;
    }

    public HealthResponse GetHealth()
    {
        var raw = RawCache();
        var now = DateTime.UtcNow;
        var uptime = now - _startedAtUtc;

        var banks = new List<BankStatus>(_providers.Length);
        foreach (var provider in _providers)
        {
            var prefix = $"{provider.Name}:";
            var maxAge = -1;
            foreach (var (key, value) in raw)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var age = (int)(now - value.FetchedAt).TotalSeconds;
                if (age > maxAge)
                    maxAge = age;
            }

            _lastErrors.TryGetValue(provider.Name, out var err);
            banks.Add(new BankStatus
            {
                Name = provider.Name,
                Status = err is null && maxAge >= 0 ? "ok" : "error",
                RateAgeSec = maxAge,
                LastError = err,
            });
        }

        return new HealthResponse
        {
            Status = banks.TrueForAll(b => b.Status == "ok") ? "healthy" : "degraded",
            Uptime = $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}",
            LastRefreshAt = _lastRefreshAtUtc.HasValue ? TimeHelper.FormatKyiv(_lastRefreshAtUtc.Value) : null,
            Banks = banks,
        };
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(_providers.Select(p => FetchSafely(p, cancellationToken)));
        var merged = new Dictionary<string, CachedBankRate>(RawCache());
        var now = DateTime.UtcNow;

        foreach (var (name, providerRates) in results)
        {
            if (providerRates is null)
                continue;

            foreach (var (currency, rate) in providerRates)
            {
                merged[BuildKey(name, currency)] = new CachedBankRate(rate, now);
                logger.LogInformation("{Provider}: 1 {Cur} = {Rate} UAH", name, currency.ToUpperInvariant(), rate);
            }
        }

        if (merged.Count > 0)
            cache.Set(CacheKey, merged);

        _lastRefreshAtUtc = now;
    }

    private async Task<(string Name, Dictionary<string, decimal>? Rates)> FetchSafely(IRateProvider provider, CancellationToken ct)
    {
        try
        {
            var result = await provider.GetRatesAsync(ct);
            _lastErrors[provider.Name] = null;
            return (provider.Name, result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed: {Provider}", provider.Name);
            _lastErrors[provider.Name] = ex.Message;
            return (provider.Name, null);
        }
    }

    private static string BuildKey(string bank, string currency) =>
        $"{bank.ToLowerInvariant()}:{currency.ToLowerInvariant()}";

    private Dictionary<string, CachedBankRate> RawCache() =>
        cache.TryGetValue(CacheKey, out Dictionary<string, CachedBankRate>? c) && c is not null ? c : [];
}
