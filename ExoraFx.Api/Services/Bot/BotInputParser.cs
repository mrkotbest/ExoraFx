using ExoraFx.Api.Helpers;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using System.Globalization;

namespace ExoraFx.Api.Services.Bot;

public enum ConversionDirection
{
    Forward,
    Reverse,
    Cross,
}

public abstract record ParseOutcome
{
    private ParseOutcome() { }

    public sealed record Success(ConvertResult Result, bool MarginOverridden, ConversionDirection Direction, bool BankExplicit) : ParseOutcome;

    public sealed record Error(string Key, object[] Args) : ParseOutcome;

    public static Error Fail(string key, params object[] args) => new(key, args);

    public static Success Ok(ConvertResult result, bool marginOverridden, ConversionDirection direction, bool bankExplicit) =>
        new(result, marginOverridden, direction, bankExplicit);
}

public sealed class BotInputParser(IConversionService conversion, IUserDefaultsResolver defaults)
{
    public ParseOutcome ParseCalculation(string text, long? userId)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 1 && CurrencyHelper.Normalize(tokens[0]) is { } onlyCur)
        {
            var amt = defaults.Amount(userId).ToString("0.##", CultureInfo.InvariantCulture);
            tokens = [amt, onlyCur];
        }

        if (tokens.Length < 2 || !TryAmount(tokens[0], out var amount))
            return ParseOutcome.Fail(BotKeys.UnknownBody);

        string? from = null;
        string? to = null;
        string? bank = null;
        decimal? marginOverride = null;
        var explicitMargin = false;

        for (var i = 1; i < tokens.Length; i++)
        {
            var raw = tokens[i];
            var t = raw.ToLowerInvariant();

            if (TryPercent(t, out var m))
            {
                if (!UserSettingsLimits.IsValidMargin(m))
                    return ParseOutcome.Fail(BotKeys.ParseMarginOutOfRange, UserSettingsLimits.MinMargin, UserSettingsLimits.MaxMargin, raw);

                marginOverride = m;
                explicitMargin = true;
                continue;
            }

            var cur = CurrencyHelper.Normalize(t);
            if (cur is not null)
            {
                if (from is null)
                {
                    from = cur;
                }
                else if (to is null)
                {
                    to = cur;
                }
                else
                {
                    return ParseOutcome.Fail(BotKeys.ParseTooManyCurrencies, raw);
                }

                continue;
            }

            if (AsBank(t) is { } b)
            {
                bank = b;
                continue;
            }

            return ParseOutcome.Fail(BotKeys.ParseUnknownToken, raw);
        }

        if (from is null)
            return ParseOutcome.Fail(BotKeys.UnknownBody);

        to ??= from == CurrencyHelper.Uah ? defaults.Currency(userId) : CurrencyHelper.Uah;
        if (from == to)
            return ParseOutcome.Fail(BotKeys.ParseSameCurrency);

        marginOverride ??= defaults.Margin(userId);

        var direction = from == CurrencyHelper.Uah
            ? ConversionDirection.Reverse
            : to == CurrencyHelper.Uah
                ? ConversionDirection.Forward
                : ConversionDirection.Cross;

        var result = direction == ConversionDirection.Reverse
            ? conversion.ConvertReverse(to, amount, bankRaw: bank, marginOverride: marginOverride)
            : conversion.Convert(from, to, amount, bankRaw: bank, marginOverride: marginOverride);

        return result is null
            ? ParseOutcome.Fail(BotKeys.ParseRateNotLoaded, from.ToUpperInvariant(), to.ToUpperInvariant())
            : ParseOutcome.Ok(result, explicitMargin, direction, bankExplicit: bank is not null);
    }

    private static bool TryAmount(string token, out decimal value) =>
        CurrencyHelper.TryParseAmount(token, out value);

    private static bool TryPercent(string token, out decimal value) =>
        CurrencyHelper.TryParsePercent(token, out value);

    private static string? AsBank(string token) =>
        CurrencyHelper.NormalizeBank(token) is { } b && b is "monobank" or "privatbank" or "nbu" ? b : null;
}
