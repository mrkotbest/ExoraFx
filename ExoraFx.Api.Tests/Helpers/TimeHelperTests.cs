using ExoraFx.Api.Helpers;
using System.Globalization;

namespace ExoraFx.Api.Tests.Helpers;

public class TimeHelperTests
{
    [Fact]
    public void FormatKyiv_ProducesExpectedPattern()
    {
        var formatted = TimeHelper.FormatKyiv(new DateTime(2026, 1, 15, 10, 30, 45, DateTimeKind.Utc));
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$", formatted);
    }

    [Fact]
    public void FormatKyiv_AppliesKyivOffset()
    {
        var formatted = TimeHelper.FormatKyiv(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var parsed = DateTime.ParseExact(formatted, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.True(parsed.Hour is 2 or 3);
    }

    [Fact]
    public void NowKyiv_ReturnsParseableTimestamp()
    {
        var nowFormatted = TimeHelper.NowKyiv();
        Assert.True(DateTime.TryParseExact(
            nowFormatted,
            "dd.MM.yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _));
    }

    [Fact]
    public void TodayKyiv_ReturnsCurrentDateInKyiv()
    {
        var today = TimeHelper.TodayKyiv();
        var nowUtc = DateTime.UtcNow;
        var diff = (today - nowUtc).TotalHours;
        Assert.True(diff >= 1 && diff <= 4);
    }

    [Fact]
    public void FormatKyiv_DateMatchesYearMonthDay()
    {
        var formatted = TimeHelper.FormatKyiv(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        Assert.StartsWith("01.01.2027", formatted);
    }
}
