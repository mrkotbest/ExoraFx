namespace ExoraFx.Api.Services.Storage;

public static class UserSettingsLimits
{
    public const decimal MinMargin = 0m;
    public const decimal MaxMargin = 50m;

    public static readonly string[] SupportedBanks = ["monobank", "privatbank", "nbu"];

    public static decimal ClampMargin(decimal percent) => Math.Clamp(percent, MinMargin, MaxMargin);

    public static bool IsValidMargin(decimal percent) => percent >= MinMargin && percent <= MaxMargin;

    public static bool IsValidBank(string? bank) => bank is not null && Array.IndexOf(SupportedBanks, bank) >= 0;
}
