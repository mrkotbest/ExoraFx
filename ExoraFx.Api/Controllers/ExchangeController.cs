using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using ExoraFx.Api.Models;
using ExoraFx.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Controllers;

[ApiController]
[Route("")]
[AllowMarginOverride]
public sealed class ExchangeController(
    IExchangeRateService rateService,
    IConversionService conversion,
    IOptions<ExchangeSettings> settings) : ControllerBase
{
    private readonly ExchangeSettings _settings = settings.Value;
    private decimal DefaultMargin => UserSettingsLimits.ClampMargin(_settings.MarginPercent);

    [HttpGet("rates")]
    public IActionResult AllRates([FromQuery] decimal? margin = null)
    {
        var m = margin ?? DefaultMargin;
        var currencies = new Dictionary<string, object>();

        foreach (var cur in _settings.SupportedCurrencies)
        {
            var all = rateService.GetAllRates(cur);
            if (all.Count == 0)
                continue;

            currencies[cur.ToUpperInvariant()] = BuildCurrencyView(all, m);
        }

        return Ok(new
        {
            marginPercent = m,
            defaultBank = _settings.DefaultBank,
            currencies,
            updatedAt = TimeHelper.NowKyiv(),
        });
    }

    [HttpGet("rates/{currency}")]
    public IActionResult RatesForCurrency(string currency, [FromQuery] decimal? margin = null)
    {
        var cur = CurrencyHelper.Normalize(currency);
        if (cur is null || cur == CurrencyHelper.Uah)
            return BadRequest(new { error = $"Unknown currency: '{currency}'" });

        var all = rateService.GetAllRates(cur);
        if (all.Count == 0)
            return StatusCode(503, new { error = $"No {cur.ToUpperInvariant()} rates loaded" });

        var m = margin ?? DefaultMargin;
        return Ok(new
        {
            currency = cur.ToUpperInvariant(),
            marginPercent = m,
            banks = all
                .OrderByDescending(kv => kv.Value.Rate)
                .Select(kv => new
                {
                    bank = kv.Key,
                    official = kv.Value.Rate,
                    yours = Math.Round(kv.Value.Rate * (1 - m / 100m), 4),
                    updatedAt = TimeHelper.FormatKyiv(kv.Value.FetchedAt),
                    ageSec = (int)(DateTime.UtcNow - kv.Value.FetchedAt).TotalSeconds,
                }),
            bestBank = BestBank(all),
            averageRate = AverageRate(all),
            updatedAt = TimeHelper.NowKyiv(),
        });
    }

    [HttpGet("convert")]
    public IActionResult Convert(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] decimal amount,
        [FromQuery] string? bank = null,
        [FromQuery] decimal? margin = null)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "'from' and 'to' are required" });

        if (amount <= 0)
            return BadRequest(new { error = "'amount' must be > 0" });

        var fromNorm = CurrencyHelper.Normalize(from);
        var toNorm = CurrencyHelper.Normalize(to);
        var result = (fromNorm == CurrencyHelper.Uah && toNorm is not null && toNorm != CurrencyHelper.Uah)
            ? conversion.ConvertReverse(to, amount, bank, margin)
            : conversion.Convert(from, to, amount, bank, margin);
        return result is null
            ? StatusCode(503, new { error = $"Cannot convert {from} → {to} at the moment" })
            : Ok(result);
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(rateService.GetHealth());

    private static object BuildCurrencyView(Dictionary<string, CachedBankRate> all, decimal marginPct)
    {
        var marginFactor = 1 - marginPct / 100m;
        return new
        {
            banks = all
                .OrderByDescending(kv => kv.Value.Rate)
                .Select(kv => new
                {
                    bank = kv.Key,
                    official = kv.Value.Rate,
                    yours = Math.Round(kv.Value.Rate * marginFactor, 4),
                    ageSec = (int)(DateTime.UtcNow - kv.Value.FetchedAt).TotalSeconds,
                }),
            best = BestBank(all),
            average = AverageRate(all),
        };
    }

    private static string BestBank(Dictionary<string, CachedBankRate> all) =>
        all.MinBy(kv => kv.Value.Rate).Key;

    private static decimal AverageRate(Dictionary<string, CachedBankRate> all) =>
        Math.Round(all.Values.Average(r => r.Rate), 4);
}
