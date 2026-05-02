using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;

namespace ExoraFx.Api.Services.Exchange;

public sealed class ConversionService(IExchangeRateService rates, IOptions<ExchangeSettings> settings) : IConversionService
{
    private readonly ExchangeSettings _settings = settings.Value;
    private decimal DefaultMargin => UserSettingsLimits.ClampMargin(_settings.MarginPercent);

    public ConvertResult? Convert(string fromRaw, string toRaw, decimal amount, string? bankRaw = null, decimal? marginOverride = null)
    {
        var from = CurrencyHelper.Normalize(fromRaw);
        var to = CurrencyHelper.Normalize(toRaw);
        if (from is null || to is null || from == to || amount <= 0)
            return null;

        var marginPct = marginOverride ?? DefaultMargin;
        var bankName = CurrencyHelper.NormalizeBank(bankRaw) ?? _settings.DefaultBank;
        var pinned = bankRaw is not null;

        var fromLeg = ResolveLeg(from, bankName, pinned);
        var toLeg = ResolveLeg(to, bankName, pinned);
        if (fromLeg is null || toLeg is null)
            return null;

        var officialToAmount = amount * fromLeg.Rate.Rate / toLeg.Rate.Rate;
        var clientToAmount = officialToAmount * (1 - marginPct / 100m);
        var profitUah = (officialToAmount - clientToAmount) * toLeg.Rate.Rate;

        var stalerFetchedAt = fromLeg.Rate.FetchedAt < toLeg.Rate.FetchedAt ? fromLeg.Rate.FetchedAt : toLeg.Rate.FetchedAt;
        var ageSec = (int)(DateTime.UtcNow - stalerFetchedAt).TotalSeconds;

        return new ConvertResult
        {
            From = from.ToUpperInvariant(),
            FromAmount = Math.Round(amount, 2),
            To = to.ToUpperInvariant(),
            ToAmount = Math.Round(clientToAmount, 2),
            EffectiveRate = Math.Round(clientToAmount / amount, 6),
            OfficialRate = Math.Round(officialToAmount / amount, 6),
            Bank = BuildBankLabel(fromLeg.Bank, toLeg.Bank),
            MarginPercent = marginPct,
            ProfitUah = Math.Round(profitUah, 2),
            ProfitEur = BuildProfitEur(profitUah, fromLeg.Bank != CurrencyHelper.Uah ? fromLeg.Bank : toLeg.Bank),
            RateAgeSec = ageSec,
            IsStale = ageSec > _settings.CacheTtlSeconds,
            CalculatedAt = TimeHelper.NowKyiv(),
        };
    }

    public ConvertResult? ConvertReverse(string foreignRaw, decimal targetUah, string? bankRaw = null, decimal? marginOverride = null)
    {
        var foreign = CurrencyHelper.Normalize(foreignRaw);
        if (foreign is null || foreign == CurrencyHelper.Uah || targetUah <= 0)
            return null;

        var marginPct = marginOverride ?? DefaultMargin;
        var bankName = CurrencyHelper.NormalizeBank(bankRaw) ?? _settings.DefaultBank;
        var pinned = bankRaw is not null;

        var leg = ResolveLeg(foreign, bankName, pinned);
        if (leg is null)
            return null;

        var officialRate = leg.Rate.Rate;
        var effectiveRate = officialRate * (1 - marginPct / 100m);
        if (effectiveRate <= 0)
            return null;

        var requiredForeign = targetUah / effectiveRate;
        var profitUah = requiredForeign * officialRate - targetUah;

        var ageSec = (int)(DateTime.UtcNow - leg.Rate.FetchedAt).TotalSeconds;

        return new ConvertResult
        {
            From = foreign.ToUpperInvariant(),
            FromAmount = Math.Round(requiredForeign, 2),
            To = "UAH",
            ToAmount = Math.Round(targetUah, 2),
            EffectiveRate = Math.Round(effectiveRate, 6),
            OfficialRate = Math.Round(officialRate, 6),
            Bank = leg.Bank,
            MarginPercent = marginPct,
            ProfitUah = Math.Round(profitUah, 2),
            ProfitEur = BuildProfitEur(profitUah, leg.Bank),
            RateAgeSec = ageSec,
            IsStale = ageSec > _settings.CacheTtlSeconds,
            CalculatedAt = TimeHelper.NowKyiv(),
        };
    }

    private decimal? BuildProfitEur(decimal profitUah, string bank)
    {
        var rate = rates.GetRate(bank, "eur");
        return rate is null ? null : Math.Round(profitUah / rate.Rate, 2);
    }

    private ResolvedLeg? ResolveLeg(string currency, string bankName, bool pinned)
    {
        if (currency == CurrencyHelper.Uah)
            return new ResolvedLeg(CurrencyHelper.Uah, new CachedBankRate(1m, DateTime.UtcNow));

        if (bankName == "average")
        {
            var all = rates.GetAllRates(currency);
            if (all.Count == 0)
                return null;

            var avg = Math.Round(all.Values.Average(r => r.Rate), 4);
            var newest = all.Values.Max(r => r.FetchedAt);
            return new ResolvedLeg("average", new CachedBankRate(avg, newest));
        }

        var rate = rates.GetRate(bankName, currency);
        if (rate is not null)
            return new ResolvedLeg(bankName, rate);

        if (pinned)
            return null;

        var candidates = rates.GetAllRates(currency);
        if (candidates.Count == 0)
            return null;

        var best = candidates.MaxBy(kv => kv.Value.Rate);
        return new ResolvedLeg(best.Key, best.Value);
    }

    private string BuildBankLabel(string fromBank, string toBank) =>
        (fromBank == CurrencyHelper.Uah, toBank == CurrencyHelper.Uah) switch
        {
            (true, true) => _settings.DefaultBank,
            (true, false) => toBank,
            (false, true) => fromBank,
            _ => fromBank == toBank ? fromBank : $"{fromBank}+{toBank}",
        };

    private sealed record ResolvedLeg(string Bank, CachedBankRate Rate);
}
