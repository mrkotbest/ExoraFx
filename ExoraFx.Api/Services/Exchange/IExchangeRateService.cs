using ExoraFx.Api.Models;

namespace ExoraFx.Api.Services.Exchange;

public interface IExchangeRateService
{
    CachedBankRate? GetRate(string bank, string currency);

    Dictionary<string, CachedBankRate> GetAllRates(string currency);

    HealthResponse GetHealth();

    Task RefreshAllAsync(CancellationToken cancellationToken = default);
}
