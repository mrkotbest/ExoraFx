using ExoraFx.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Services.Background;

public sealed class RateRefreshBackgroundService(
    IExchangeRateService rateService,
    IOptions<ExchangeSettings> settings,
    ILogger<RateRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(settings.Value.RefreshIntervalSeconds);
        logger.LogInformation("Rate refresh started. Interval: {Sec}s", settings.Value.RefreshIntervalSeconds);

        await Safe(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await Safe(stoppingToken);
        }
    }

    private async Task Safe(CancellationToken ct)
    {
        try
        {
            await rateService.RefreshAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rate refresh failed");
        }
    }
}
