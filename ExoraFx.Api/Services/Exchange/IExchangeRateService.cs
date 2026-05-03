using ExoraFx.Api.Models;

namespace ExoraFx.Api.Services.Exchange;

public interface IExchangeRateService
{
    CachedBankRate? GetRate(string bank, string currency);

    Dictionary<string, CachedBankRate> GetAllRates(string currency);

    string? FindBestBank(string fromCurrency, string toCurrency);

    HealthResponse GetHealth();

    Task RefreshAllAsync(CancellationToken cancellationToken = default);
}
