namespace ExoraFx.Api.Helpers;

public static class TimeHelper
{
    private const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    private static readonly TimeZoneInfo KyivZone = ResolveKyiv();

    private static TimeZoneInfo ResolveKyiv()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
        }
    }

    public static string FormatKyiv(DateTime utcTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), KyivZone).ToString(DateFormat);

    public static string NowKyiv() => FormatKyiv(DateTime.UtcNow);

    public static DateTime TodayKyiv() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), KyivZone);
}
