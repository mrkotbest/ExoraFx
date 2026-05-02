using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace ExoraFx.Api.Services.Bot;

public sealed class BotMessageRenderer(
    IBotLocalizer localizer,
    IConversionService conversion,
    IUserDefaultsResolver defaults,
    IExchangeRateService rateService,
    IOptions<ExchangeSettings> exchangeSettings)
{
    private static readonly decimal[] TableAmounts = [5m, 10m, 20m, 50m, 100m, 200m, 500m, 1000m, 2500m, 5000m];
    private static readonly decimal[] DefaultScenarioMargins = [1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m, 13m, 14m, 15m];

    private readonly ExchangeSettings _exchange = exchangeSettings.Value;

    public string FormatConvert(ConvertResult r, bool marginOverridden, string? lang, bool showBestHint)
    {
        var stale = r.IsStale ? localizer.Get(BotKeys.ConvertStaleSuffix, lang) : "";
        var marginNote = marginOverridden ? localizer.Get(BotKeys.ConvertOverriddenSuffix, lang) : "";
        var fromL = r.From.ToLowerInvariant();
        var toL = r.To.ToLowerInvariant();

        var profitLine = $"*{CurrencyHelper.FormatAmount(r.ProfitUah, 2)} ₴*";
        if (r.ProfitEur is { } eur)
            profitLine += $" · *{CurrencyHelper.FormatAmount(eur, 2)} €*";

        var bestBank = ResolveBestBank(r);
        var bankCup = bestBank is not null && bestBank == r.Bank ? " 🏆" : "";

        var body =
            $"{CurrencyHelper.Flag(fromL)} *{CurrencyHelper.FormatAmount(r.FromAmount, 2)} {r.From}* ({CurrencyHelper.Symbol(fromL)}) → "
            + $"{CurrencyHelper.Flag(toL)} *{CurrencyHelper.FormatAmount(r.ToAmount, 2)} {r.To}* ({CurrencyHelper.Symbol(toL)})\n\n"
            + $"{localizer.Get(BotKeys.ConvertOfficialLabel, lang)}: `{CurrencyHelper.FormatAmount(r.OfficialRate, 4)}`\n"
            + $"{localizer.Get(BotKeys.ConvertOurRateLabel, lang)}: `{CurrencyHelper.FormatAmount(r.EffectiveRate, 4)}`\n"
            + $"{localizer.Get(BotKeys.ConvertMarginLabel, lang)}: *{CurrencyHelper.FormatAmount(r.MarginPercent, 2)}%*{marginNote}\n"
            + $"{localizer.Get(BotKeys.ConvertProfitLabel, lang)}: {profitLine}\n\n"
            + $"{localizer.Get(BotKeys.ConvertBankLabel, lang)}: `{r.Bank}`{bankCup}{stale}";

        var hint = BuildBetterBankHint(r, bestBank, showBestHint, lang);
        return hint is null ? body : body + "\n\n" + hint;
    }

    private string? ResolveBestBank(ConvertResult r)
    {
        var fromL = r.From.ToLowerInvariant();
        var toL = r.To.ToLowerInvariant();
        if (fromL == toL || (fromL != CurrencyHelper.Uah && toL != CurrencyHelper.Uah))
            return null;
        var foreign = fromL == CurrencyHelper.Uah ? toL : fromL;
        var allRates = rateService.GetAllRates(foreign);
        return allRates.Count == 0 ? null : allRates.MinBy(kv => kv.Value.Rate).Key;
    }

    private string? BuildBetterBankHint(ConvertResult r, string? bestBank, bool showBestHint, string? lang)
    {
        if (!showBestHint || bestBank is null || bestBank == r.Bank)
            return null;

        var fromL = r.From.ToLowerInvariant();
        var toL = r.To.ToLowerInvariant();
        var isReverse = fromL == CurrencyHelper.Uah && toL != CurrencyHelper.Uah;
        var isForward = toL == CurrencyHelper.Uah && fromL != CurrencyHelper.Uah;
        if (!isForward && !isReverse)
            return null;

        if (isForward)
        {
            var foreign = fromL;
            var alt = conversion.Convert(foreign, CurrencyHelper.Uah, r.FromAmount, bestBank, r.MarginPercent);
            if (alt is null)
                return null;
            var deltaUah = alt.ToAmount - r.ToAmount;
            if (deltaUah <= 0.005m)
                return null;
            return localizer.Get(BotKeys.ConvertBestHintBetter, lang, bestBank, CurrencyHelper.FormatAmount(deltaUah, 2));
        }

        return localizer.Get(BotKeys.ConvertBestHintBetterReverse, lang, bestBank);
    }

    public string FormatRates(long? userId, string? lang)
    {
        var marginPct = defaults.Margin(userId);
        var marginFactor = 1 - marginPct / 100m;
        var lines = new List<string> { localizer.Get(BotKeys.RatesTitle, lang, CurrencyHelper.FormatAmount(marginPct, 2)) };
        var addedAny = false;

        foreach (var cur in _exchange.SupportedCurrencies)
        {
            var all = rateService.GetAllRates(cur);
            if (all.Count == 0)
                continue;

            addedAny = true;

            var minBank = all.MinBy(kv => kv.Value.Rate).Key;

            lines.Add($"\n{CurrencyHelper.Flag(cur)} *{cur.ToUpperInvariant()}* ({CurrencyHelper.Symbol(cur)})");

            foreach (var (bank, rate) in all.OrderBy(kv => kv.Value.Rate))
            {
                var adjusted = Math.Round(rate.Rate * marginFactor, 2);
                var marker = bank == minBank ? " 🏆" : "";
                lines.Add($"  · `{bank}` {CurrencyHelper.FormatAmount(adjusted, 2)} ₴{marker}");
            }
        }

        return addedAny ? string.Join("\n", lines) : localizer.Get(BotKeys.RatesEmpty, lang);
    }

    public TableRender BuildTable(long? userId, string? currency, string? bank, string? lang)
    {
        var cur = currency ?? defaults.Currency(userId);
        var bk = bank ?? defaults.Bank(userId);
        var userMargin = defaults.Margin(userId);

        return BuildGrid(
            TableAmounts, cur, bk,
            getAmount: a => a,
            getMargin: _ => userMargin,
            firstCol: a => Format(a, "F0"),
            colA: localizer.Get(BotKeys.TableColAmount, lang),
            colB: localizer.Get(BotKeys.TableColResult, lang),
            colC: localizer.Get(BotKeys.TableColProfit, lang),
            formatHeader: effBank => localizer.Get(
                BotKeys.TableHeader, lang,
                CurrencyHelper.Flag(cur),
                cur.ToUpperInvariant(),
                effBank,
                CurrencyHelper.FormatAmount(userMargin, 2)),
            noRateText: localizer.Get(BotKeys.TableNoRate, lang, cur.ToUpperInvariant()));
    }

    public TableRender BuildScenario(long? userId, string? currency, string? bank, string? lang)
    {
        var cur = currency ?? defaults.Currency(userId);
        var bk = bank ?? defaults.Bank(userId);
        var sum = defaults.Amount(userId);

        return BuildGrid(
            DefaultScenarioMargins, cur, bk,
            getAmount: _ => sum,
            getMargin: m => m,
            firstCol: m => Format(m, "0.##") + "%",
            colA: localizer.Get(BotKeys.ScenarioColMargin, lang),
            colB: localizer.Get(BotKeys.ScenarioColResult, lang),
            colC: localizer.Get(BotKeys.ScenarioColProfit, lang),
            formatHeader: effBank => localizer.Get(
                BotKeys.ScenarioHeader, lang,
                CurrencyHelper.Flag(cur),
                CurrencyHelper.FormatAmount(sum, 2),
                cur.ToUpperInvariant(),
                effBank),
            noRateText: localizer.Get(BotKeys.ScenarioNoRate, lang, cur.ToUpperInvariant()));
    }

    private TableRender BuildGrid<T>(
        IReadOnlyList<T> series,
        string cur, string bank,
        Func<T, decimal> getAmount,
        Func<T, decimal> getMargin,
        Func<T, string> firstCol,
        string colA, string colB, string colC,
        Func<string, string> formatHeader,
        string noRateText)
    {
        var rows = new List<string>(series.Count + 1) { Row3(colA, colB, colC) };
        string? effectiveBank = null;

        foreach (var item in series)
        {
            var r = conversion.Convert(cur, CurrencyHelper.Uah, getAmount(item), bankRaw: bank, marginOverride: getMargin(item));
            if (r is null)
                continue;

            effectiveBank ??= r.Bank;
            rows.Add(Row3(firstCol(item), Format(r.ToAmount, "F2"), FormatProfit(r.ProfitUah, r.ProfitEur)));
        }

        if (rows.Count == 1)
            return new TableRender(noRateText, cur, bank);

        var resolvedBank = effectiveBank ?? bank;
        return new TableRender(formatHeader(resolvedBank) + "\n" + string.Join("\n", rows), cur, resolvedBank);
    }

    public string FormatHistoryStats(HistoryStats stats, string period, string? lang)
    {
        var periodLabel = localizer.Get(period switch
        {
            StatsPeriod.Today => BotKeys.KbStatsToday,
            StatsPeriod.Week => BotKeys.KbStatsWeek,
            StatsPeriod.Month => BotKeys.KbStatsMonth,
            _ => BotKeys.KbStatsAll,
        }, lang);

        if (stats.TotalCount == 0)
        {
            var head = localizer.Get(BotKeys.HistoryStatsHeader, lang);
            var empty = localizer.Get(BotKeys.HistoryStatsEmpty, lang);
            return $"{head}\n\n{empty}";
        }

        var lines = new List<string>
        {
            localizer.Get(BotKeys.HistoryStatsHeader, lang),
            localizer.Get(BotKeys.HistoryStatsPeriodLabel, lang, periodLabel),
            "",
            localizer.Get(BotKeys.HistoryStatsCounts, lang, stats.TotalCount, stats.DoneCount, stats.DraftCount),
        };

        if (stats.FirstAtUtc is { } first && stats.LastAtUtc is { } last)
        {
            lines.Add(localizer.Get(
                BotKeys.HistoryStatsPeriod, lang,
                first.ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture),
                last.ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture)));
        }

        if (stats.ReceivedByCurrency.Count > 0)
        {
            lines.Add("");
            lines.Add(localizer.Get(BotKeys.HistoryStatsVolumeTitle, lang));
            foreach (var (cur, amount) in stats.ReceivedByCurrency.OrderByDescending(kv => kv.Value))
            {
                lines.Add(localizer.Get(
                    BotKeys.HistoryStatsVolumeRow, lang,
                    CurrencyHelper.Flag(cur),
                    cur.ToUpperInvariant(),
                    CurrencyHelper.FormatAmount(amount, 2),
                    CurrencyHelper.Symbol(cur)));
            }
        }

        lines.Add("");
        lines.Add(localizer.Get(BotKeys.HistoryStatsUahTitle, lang, CurrencyHelper.FormatAmount(stats.PaidUah, 2)));

        var profitEurTail = stats.ProfitEur > 0
            ? $" · *{CurrencyHelper.FormatAmount(stats.ProfitEur, 2)} €*"
            : "";
        lines.Add(localizer.Get(BotKeys.HistoryStatsProfit, lang, CurrencyHelper.FormatAmount(stats.ProfitUah, 2), profitEurTail));

        lines.Add(localizer.Get(BotKeys.HistoryStatsAvgMargin, lang, CurrencyHelper.FormatAmount(stats.AverageMarginPercent, 2)));

        if (stats.TopBank is { } bank)
            lines.Add(localizer.Get(BotKeys.HistoryStatsTopBank, lang, bank, stats.TopBankCount));

        if (stats.TopCurrency is { } topCur)
            lines.Add(localizer.Get(BotKeys.HistoryStatsTopCurrency, lang, $"{CurrencyHelper.Flag(topCur)} {topCur.ToUpperInvariant()}"));

        if (stats.MaxTradeFromCurrency is { } maxCur && stats.MaxTradeUah > 0)
        {
            lines.Add(localizer.Get(
                BotKeys.HistoryStatsMaxTrade, lang,
                CurrencyHelper.FormatAmount(stats.MaxTradeFromAmount, 2),
                maxCur.ToUpperInvariant(),
                CurrencyHelper.FormatAmount(stats.MaxTradeUah, 2)));
        }

        return string.Join("\n", lines);
    }

    private static string FormatProfit(decimal profitUah, decimal? profitEur)
    {
        var uah = profitUah.ToString("F2", CultureInfo.InvariantCulture);
        return profitEur is { } e
            ? $"{uah}/{e.ToString("F2", CultureInfo.InvariantCulture)}"
            : uah + " ₴";
    }

    private static string Row3(string a, string b, string c) =>
        $"`{Pad(a, 6)} {Pad(b, 10)} {c}`";

    private static string Pad(string text, int width) =>
        text.Length >= width ? text : text + new string(' ', width - text.Length);

    private static string Format(decimal value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}

public readonly record struct TableRender(string Text, string Currency, string Bank);
