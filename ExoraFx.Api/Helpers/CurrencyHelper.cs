using System.Globalization;

namespace ExoraFx.Api.Helpers;

public static class CurrencyHelper
{
    public const string Uah = "uah";

    public static string? Normalize(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "eur" or "euro" or "e" or "е" or "є" or "єв" or "ев" or "евр" or "евро" or "євро" or "эур" or "€"
            => "eur",

        "usd" or "dollar" or "dollars" or "d" or "д" or "дол" or "долар" or "долари" or "доллар" or "доллары" or "бак" or "бакс" or "баксы" or "$"
            => "usd",

        "pln" or "pl" or "z" or "з" or "zl" or "zł" or "zloty" or "złoty" or "злот" or "злотый" or "злотые" or "злотих"
            => "pln",

        "uah" or "uan" or "грн" or "грив" or "гривна" or "гривен" or "гривня" or "гривні" or "гривень" or "hryvnia" or "hrn" or "₴"
            => Uah,

        _ => null,
    };

    public static string Flag(string currency) => currency.ToLowerInvariant() switch
    {
        "eur" => "🇪🇺",
        "usd" => "🇺🇸",
        "pln" => "🇵🇱",
        "uah" => "🇺🇦",
        _ => "",
    };

    public static string Symbol(string currency) => currency.ToLowerInvariant() switch
    {
        "eur" => "€",
        "usd" => "$",
        "pln" => "zł",
        "uah" => "₴",
        _ => "",
    };

    public static string? NormalizeBank(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "mono" or "monobank" or "моно" => "monobank",
        "privat" or "privatbank" or "приват" => "privatbank",
        "nbu" or "нбу" => "nbu",
        "avg" or "average" or "среднее" => "average",
        _ => null,
    };

    public static string FormatAmount(decimal value, int decimals = 2)
    {
        var rounded = Math.Round(value, decimals);
        return rounded == Math.Truncate(rounded)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static bool TryParseDecimal(string token, out decimal value) =>
        decimal.TryParse(token.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    public static bool TryParsePercent(string token, out decimal value) =>
        TryParseDecimal(token.TrimEnd('%'), out value);

    public static bool TryParseAmount(string token, out decimal value)
    {
        var t = token.Replace(',', '.');
        var multiplier = 1m;
        if (t.Length > 0)
        {
            var last = char.ToLowerInvariant(t[^1]);
            if (last is 'к' or 'k')
            {
                multiplier = 1000m;
                t = t[..^1];
            }
        }

        if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            value *= multiplier;
            return true;
        }

        value = 0;
        return false;
    }
}
