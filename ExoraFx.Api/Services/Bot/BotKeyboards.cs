using ExoraFx.Api.Helpers;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using System.Globalization;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExoraFx.Api.Services.Bot;

public sealed class BotKeyboards(IBotLocalizer localizer, ILanguageValidator languageValidator, IExchangeRateService rateService)
{
    public static readonly string[] Banks = ["monobank", "privatbank", "nbu"];
    public static readonly string[] Currencies = ["eur", "usd", "pln"];
    public static readonly string[] Languages = ["ru", "uk", "en"];

    private static readonly decimal[] MarginPresets = [3m, 5m, 7m, 10m];
    private static readonly decimal[] AmountPresets = [50m, 100m, 500m, 1000m];

    public InlineKeyboardMarkup? Convert(ConvertResult result, ConversionDirection direction, string? lang, long entryId, bool expanded, bool isDone)
    {
        if (direction == ConversionDirection.Cross)
            return null;

        var dir = direction == ConversionDirection.Forward ? 'f' : 'r';
        var foreign = result.From.ToLowerInvariant();
        var amount = direction == ConversionDirection.Forward ? result.FromAmount : result.ToAmount;
        var amountStr = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var marginStr = result.MarginPercent.ToString("0.##", CultureInfo.InvariantCulture);
        var mode = expanded ? 'e' : 'c';

        if (!expanded)
        {
            var actionRow = new List<InlineKeyboardButton>(2);
            if (entryId > 0)
            {
                var markLabel = localizer.Get(isDone ? BotKeys.KbConvertUnmark : BotKeys.KbConvertMark, lang);
                actionRow.Add(InlineKeyboardButton.WithCallbackData(markLabel, $"cmk|{entryId}|{dir}|{foreign}|{amountStr}|{result.Bank}|{marginStr}|{mode}"));
            }
            actionRow.Add(InlineKeyboardButton.WithCallbackData(
                localizer.Get(BotKeys.KbConvertTune, lang),
                $"cv|e|{dir}|{foreign}|{amountStr}|{result.Bank}|{marginStr}|{entryId}|{(isDone ? 1 : 0)}"));
            return new InlineKeyboardMarkup([actionRow.ToArray()]);
        }

        var bestBank = rateService.FindBestBank(result.From, result.To);

        var bankRow = Banks
            .Select(b => InlineKeyboardButton.WithCallbackData(
                FormatBankLabel(b, current: result.Bank, best: bestBank),
                ConvertBank(dir, foreign, amountStr, result.Bank, marginStr, b, entryId, isDone)))
            .Append(InlineKeyboardButton.WithCallbackData(
                localizer.Get(BotKeys.KbDefaultBank, lang),
                ConvertBank(dir, foreign, amountStr, result.Bank, marginStr, "d", entryId, isDone)))
            .ToArray();

        var marginRow = new[]
        {
            InlineKeyboardButton.WithCallbackData("−1%", ConvertMargin(dir, foreign, amountStr, result.Bank, marginStr, ShiftMargin(result.MarginPercent, -1m), entryId, isDone)),
            InlineKeyboardButton.WithCallbackData("+1%", ConvertMargin(dir, foreign, amountStr, result.Bank, marginStr, ShiftMargin(result.MarginPercent, +1m), entryId, isDone)),
            InlineKeyboardButton.WithCallbackData(
                localizer.Get(BotKeys.KbDefaultMargin, lang),
                ConvertMargin(dir, foreign, amountStr, result.Bank, marginStr, "d", entryId, isDone)),
        };

        var collapseRow = new[]
        {
            InlineKeyboardButton.WithCallbackData(
                localizer.Get(BotKeys.KbConvertCollapse, lang),
                $"cv|c|{dir}|{foreign}|{amountStr}|{result.Bank}|{marginStr}|{entryId}|{(isDone ? 1 : 0)}"),
        };

        return new InlineKeyboardMarkup([bankRow, marginRow, collapseRow]);
    }

    private static string FormatBankLabel(string bank, string current, string? best)
    {
        var label = ButtonBank(bank);
        _ = best;
        return bank == current ? $"✓ {label}" : label;
    }

    public InlineKeyboardMarkup Rates(string? lang) =>
        new(InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbRefresh, lang), "rr"));

    public InlineKeyboardMarkup TableLike(string currentCurrency, string currentBank, string screen, string? lang)
    {
        var bankRow = Banks
            .Select(b => InlineKeyboardButton.WithCallbackData(
                b == currentBank ? $"✓ {ButtonBank(b)}" : ButtonBank(b),
                $"{screen}|{currentCurrency}|{b}"))
            .ToArray();

        var currencyRow = Currencies
            .Select(c => InlineKeyboardButton.WithCallbackData(
                c == currentCurrency ? $"✓ {c.ToUpperInvariant()}" : c.ToUpperInvariant(),
                $"{screen}|{c}|{currentBank}"))
            .ToArray();

        return new InlineKeyboardMarkup([bankRow, currencyRow]);
    }

    public InlineKeyboardMarkup? History(IReadOnlyList<HistoryEntry> entries, int offset, int pageSize, int total, string? lang, bool includeBack)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (entries.Count > 0)
        {
            var toggles = new InlineKeyboardButton[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var label = e.State == HistoryState.Done ? $"🟢 #{i + 1}" : $"⏳ #{i + 1}";
                toggles[i] = InlineKeyboardButton.WithCallbackData(label, $"htg|{e.Id}|{offset}");
            }
            rows.Add(toggles);
        }

        var hasPrev = offset > 0;
        var hasNext = offset + pageSize < total;
        if (hasPrev || hasNext)
        {
            var nav = new List<InlineKeyboardButton>(2);
            if (hasPrev)
                nav.Add(InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbHistoryPrev, lang), $"hp|{Math.Max(0, offset - pageSize)}"));
            if (hasNext)
                nav.Add(InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbHistoryNext, lang), $"hp|{offset + pageSize}"));
            rows.Add(nav.ToArray());
        }

        if (includeBack)
            rows.Add([InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|history")]);

        return rows.Count == 0 ? null : new InlineKeyboardMarkup(rows);
    }

    public InlineKeyboardMarkup StatsBackOnly(string? lang) =>
        new(InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|history"));

    public InlineKeyboardMarkup BackToHistory(string? lang) =>
        new(InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|history"));

    public InlineKeyboardMarkup Stats(string currentPeriod, string? lang, bool includeBack)
    {
        var (today, week, month, all) = (
            localizer.Get(BotKeys.KbStatsToday, lang),
            localizer.Get(BotKeys.KbStatsWeek, lang),
            localizer.Get(BotKeys.KbStatsMonth, lang),
            localizer.Get(BotKeys.KbStatsAll, lang));

        string Mark(string period, string label) => period == currentPeriod ? "✓ " + label : label;

        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Mark(StatsPeriod.Today, today), $"hs|{StatsPeriod.Today}"),
                InlineKeyboardButton.WithCallbackData(Mark(StatsPeriod.Week, week), $"hs|{StatsPeriod.Week}"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(Mark(StatsPeriod.Month, month), $"hs|{StatsPeriod.Month}"),
                InlineKeyboardButton.WithCallbackData(Mark(StatsPeriod.All, all), $"hs|{StatsPeriod.All}"),
            },
        };
        if (includeBack)
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|history") });

        return new InlineKeyboardMarkup(rows);
    }

    public InlineKeyboardMarkup SettingsMain(UserSettings current, string? lang)
    {
        var bestHint = current.ShowBestHint ?? true;
        var hintLabel = localizer.Get(bestHint ? BotKeys.SettingsBtnBestHintOn : BotKeys.SettingsBtnBestHintOff, lang);
        var hintData = bestHint ? "bh|off" : "bh|on";

        return new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnLang, lang), "so|lang"),
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnMargin, lang), "so|margin"),
            ],
            [
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnBank, lang), "so|bank"),
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnCurrency, lang), "so|currency"),
            ],
            [
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnAmount, lang), "so|amount"),
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.SettingsBtnHistory, lang), "so|history"),
            ],
            [InlineKeyboardButton.WithCallbackData(hintLabel, hintData)],
            [InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbResetAll, lang), "sr")],
        ]);
    }

    public InlineKeyboardMarkup SettingsLang(UserSettings current, string? lang) =>
        BuildPresetMenu("lang", Languages, l => l == current.Language, l => l, l => l, withCustom: false, lang);

    public InlineKeyboardMarkup SettingsBank(UserSettings current, string? lang) =>
        BuildPresetMenu("bank", Banks, b => b == current.DefaultBank, ButtonBank, b => b, withCustom: false, lang);

    public InlineKeyboardMarkup SettingsCurrency(UserSettings current, string? lang) =>
        BuildPresetMenu("currency", Currencies, c => c == current.DefaultCurrency, c => c.ToUpperInvariant(), c => c, withCustom: false, lang);

    public InlineKeyboardMarkup SettingsMargin(UserSettings current, string? lang) =>
        BuildPresetMenu("margin", MarginPresets, p => current.MarginPercent == p, p => Format(p) + "%", Format, withCustom: true, lang);

    public InlineKeyboardMarkup SettingsAmount(UserSettings current, string? lang) =>
        BuildPresetMenu("amount", AmountPresets, p => current.DefaultAmount == p, Format, Format, withCustom: true, lang);

    private InlineKeyboardMarkup BuildPresetMenu<T>(
        string field,
        IReadOnlyList<T> items,
        Func<T, bool> isCurrent,
        Func<T, string> label,
        Func<T, string> callbackValue,
        bool withCustom,
        string? lang)
    {
        var presets = new InlineKeyboardButton[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var text = isCurrent(item) ? "✓ " + label(item) : label(item);
            presets[i] = InlineKeyboardButton.WithCallbackData(text, $"sa|{field}|{callbackValue(item)}");
        }

        if (!withCustom)
            return new InlineKeyboardMarkup([presets, DefaultBackRow(field, lang)]);

        var customRow = new[]
        {
            InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbCustom, lang), "sp|" + field),
        };
        return new InlineKeyboardMarkup([presets, customRow, DefaultBackRow(field, lang)]);
    }

    public InlineKeyboardMarkup SettingsHistory(UserSettings current, string? lang)
    {
        var enabled = current.HistoryEnabled ?? true;
        var toggleLabel = localizer.Get(enabled ? BotKeys.KbHistoryRecordingOn : BotKeys.KbHistoryRecordingOff, lang);
        var toggleData = enabled ? "ht|off" : "ht|on";

        return new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbShowHistory, lang), "hp|0"),
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbHistoryStats, lang), "hs|all"),
            ],
            [
                InlineKeyboardButton.WithCallbackData(toggleLabel, toggleData),
                InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbClearHistory, lang), "hc"),
            ],
            [InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|menu")],
        ]);
    }

    public static string ButtonBank(string bank) => bank switch
    {
        "monobank" => "mono",
        "privatbank" => "privat",
        "nbu" => "nbu",
        _ => bank,
    };

    public string FullBank(string bank, string? lang) => bank switch
    {
        "monobank" => "Monobank",
        "privatbank" => "PrivatBank",
        "nbu" => string.Equals(languageValidator.ResolveLanguage(lang), "en", StringComparison.OrdinalIgnoreCase) ? "NBU" : "НБУ",
        _ => bank,
    };

    private InlineKeyboardButton[] DefaultBackRow(string field, string? lang) =>
    [
        InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbDefault, lang), "sa|" + field + "|d"),
        InlineKeyboardButton.WithCallbackData(localizer.Get(BotKeys.KbBack, lang), "so|menu"),
    ];

    private static string ConvertBank(char dir, string foreign, string amount, string currentBank, string margin, string newBank, long entryId, bool isDone) =>
        $"cb|{dir}|{foreign}|{amount}|{currentBank}|{margin}|{newBank}|{entryId}|{(isDone ? 1 : 0)}";

    private static string ConvertMargin(char dir, string foreign, string amount, string bank, string currentMargin, string newMargin, long entryId, bool isDone) =>
        $"cm|{dir}|{foreign}|{amount}|{bank}|{currentMargin}|{newMargin}|{entryId}|{(isDone ? 1 : 0)}";

    private static string ShiftMargin(decimal current, decimal delta)
    {
        var shifted = UserSettingsLimits.ClampMargin(current + delta);
        return Format(shifted);
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public abstract record CallbackData
{
    private CallbackData() { }

    public sealed record ConvertBank(char Direction, string Foreign, decimal Amount, string CurrentBank, decimal CurrentMargin, string NewBank, long EntryId, bool IsDone) : CallbackData;
    public sealed record ConvertMargin(char Direction, string Foreign, decimal Amount, string Bank, decimal CurrentMargin, string NewMarginToken, long EntryId, bool IsDone) : CallbackData;
    public sealed record ConvertView(bool Expanded, char Direction, string Foreign, decimal Amount, string Bank, decimal Margin, long EntryId, bool IsDone) : CallbackData;
    public sealed record ConvertMark(long EntryId, char Direction, string Foreign, decimal Amount, string Bank, decimal Margin, bool Expanded) : CallbackData;
    public sealed record RatesRefresh : CallbackData;
    public sealed record SettingsOpen(string Field) : CallbackData;
    public sealed record SettingsApply(string Field, string Value) : CallbackData;
    public sealed record SettingsCustomPrompt(string Field) : CallbackData;
    public sealed record SettingsResetAll : CallbackData;
    public sealed record TableModify(string Currency, string Bank) : CallbackData;
    public sealed record ScenarioModify(string Currency, string Bank) : CallbackData;
    public sealed record HistoryPage(int Offset) : CallbackData;
    public sealed record HistoryClear : CallbackData;
    public sealed record HistoryToggle(bool Enabled) : CallbackData;
    public sealed record BestHintToggle(bool Enabled) : CallbackData;
    public sealed record HistoryStatsOpen(string Period) : CallbackData;
    public sealed record HistoryEntryToggle(long EntryId, int PageOffset) : CallbackData;
    public sealed record Unknown(string Raw) : CallbackData;

    public static CallbackData Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new Unknown(raw);

        var parts = raw.Split('|');
        return parts switch
        {
            ["rr"] => new RatesRefresh(),
            ["sr"] => new SettingsResetAll(),
            ["hc"] => new HistoryClear(),
            ["hs", var period] when StatsPeriod.IsValid(period) => new HistoryStatsOpen(period),
            ["ht", "on"] => new HistoryToggle(true),
            ["ht", "off"] => new HistoryToggle(false),
            ["bh", "on"] => new BestHintToggle(true),
            ["bh", "off"] => new BestHintToggle(false),
            ["hp", var off] when int.TryParse(off, NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) => new HistoryPage(o),
            ["htg", var eid, var off]
                when long.TryParse(eid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                    && int.TryParse(off, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageOff)
                => new HistoryEntryToggle(entryId, pageOff),
            ["so", var field] => new SettingsOpen(field),
            ["sa", var field, var value] => new SettingsApply(field, value),
            ["sp", var field] => new SettingsCustomPrompt(field),
            ["tt", var c, var b] => new TableModify(c, b),
            ["ts", var c, var b] => new ScenarioModify(c, b),
            ["cb", [var dir], var foreign, var amount, var bank, var margin, var newBank, var eid, var done]
                when TryDecimal(amount, out var a) && TryDecimal(margin, out var m)
                    && long.TryParse(eid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                => new ConvertBank(dir, foreign, a, bank, m, newBank, entryId, done == "1"),
            ["cm", [var dir], var foreign, var amount, var bank, var margin, var newMargin, var eid, var done]
                when TryDecimal(amount, out var a) && TryDecimal(margin, out var m)
                    && long.TryParse(eid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                => new ConvertMargin(dir, foreign, a, bank, m, newMargin, entryId, done == "1"),
            ["cv", var modeStr, [var dir], var foreign, var amount, var bank, var margin, var eid, var done]
                when (modeStr == "e" || modeStr == "c")
                    && TryDecimal(amount, out var a) && TryDecimal(margin, out var m)
                    && long.TryParse(eid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                => new ConvertView(modeStr == "e", dir, foreign, a, bank, m, entryId, done == "1"),
            ["cmk", var eid, [var dir], var foreign, var amount, var bank, var margin, var modeStr]
                when long.TryParse(eid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId)
                    && TryDecimal(amount, out var a) && TryDecimal(margin, out var m)
                    && (modeStr == "e" || modeStr == "c")
                => new ConvertMark(entryId, dir, foreign, a, bank, m, modeStr == "e"),
            _ => new Unknown(raw),
        };
    }

    private static bool TryDecimal(string s, out decimal value) =>
        CurrencyHelper.TryParseDecimal(s, out value);
}
