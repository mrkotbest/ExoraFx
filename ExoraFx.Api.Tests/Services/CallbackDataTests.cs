using ExoraFx.Api.Models;

namespace ExoraFx.Api.Tests.Services;

public class CallbackDataTests
{
    [Fact]
    public void Parse_RatesRefresh()
    {
        Assert.IsType<CallbackData.RatesRefresh>(CallbackData.Parse("rr"));
    }

    [Fact]
    public void Parse_SettingsResetAll()
    {
        Assert.IsType<CallbackData.SettingsResetAll>(CallbackData.Parse("sr"));
    }

    [Fact]
    public void Parse_SettingsOpen_ExtractsField()
    {
        var parsed = Assert.IsType<CallbackData.SettingsOpen>(CallbackData.Parse("so|lang"));
        Assert.Equal("lang", parsed.Field);
    }

    [Fact]
    public void Parse_SettingsApply_ExtractsFieldAndValue()
    {
        var parsed = Assert.IsType<CallbackData.SettingsApply>(CallbackData.Parse("sa|bank|monobank"));
        Assert.Equal("bank", parsed.Field);
        Assert.Equal("monobank", parsed.Value);
    }

    [Fact]
    public void Parse_SettingsApply_DefaultMarker_PassesThrough()
    {
        var parsed = Assert.IsType<CallbackData.SettingsApply>(CallbackData.Parse("sa|margin|d"));
        Assert.Equal("margin", parsed.Field);
        Assert.Equal("d", parsed.Value);
    }

    [Fact]
    public void Parse_SettingsCustomPrompt_ExtractsField()
    {
        var parsed = Assert.IsType<CallbackData.SettingsCustomPrompt>(CallbackData.Parse("sp|amount"));
        Assert.Equal("amount", parsed.Field);
    }

    [Fact]
    public void Parse_ConvertBank_ExtractsAllFields()
    {
        var parsed = Assert.IsType<CallbackData.ConvertBank>(CallbackData.Parse("cb|f|eur|5000.00|monobank|7.7|privatbank|42|1"));
        Assert.Equal('f', parsed.Direction);
        Assert.Equal("eur", parsed.Foreign);
        Assert.Equal(5000.00m, parsed.Amount);
        Assert.Equal("monobank", parsed.CurrentBank);
        Assert.Equal(7.7m, parsed.CurrentMargin);
        Assert.Equal("privatbank", parsed.NewBank);
        Assert.Equal(42L, parsed.EntryId);
        Assert.True(parsed.IsDone);
    }

    [Fact]
    public void Parse_ConvertBank_DefaultBankToken_PreservedAsLiteral()
    {
        var parsed = Assert.IsType<CallbackData.ConvertBank>(CallbackData.Parse("cb|f|eur|100|monobank|7.7|d|0|0"));
        Assert.Equal("d", parsed.NewBank);
        Assert.Equal(0L, parsed.EntryId);
        Assert.False(parsed.IsDone);
    }

    [Fact]
    public void Parse_ConvertMargin_ExtractsAllFields()
    {
        var parsed = Assert.IsType<CallbackData.ConvertMargin>(CallbackData.Parse("cm|r|usd|100|nbu|5|6.5|17|0"));
        Assert.Equal('r', parsed.Direction);
        Assert.Equal("usd", parsed.Foreign);
        Assert.Equal(100m, parsed.Amount);
        Assert.Equal("nbu", parsed.Bank);
        Assert.Equal(5m, parsed.CurrentMargin);
        Assert.Equal("6.5", parsed.NewMarginToken);
        Assert.Equal(17L, parsed.EntryId);
        Assert.False(parsed.IsDone);
    }

    [Fact]
    public void Parse_ConvertMargin_DefaultToken_PreservedAsLiteral()
    {
        var parsed = Assert.IsType<CallbackData.ConvertMargin>(CallbackData.Parse("cm|f|eur|100|monobank|7.7|d|0|0"));
        Assert.Equal("d", parsed.NewMarginToken);
    }

    [Fact]
    public void Parse_ConvertView_ExpandedAndCollapsed()
    {
        var ex = Assert.IsType<CallbackData.ConvertView>(CallbackData.Parse("cv|e|f|eur|100|monobank|7.7|42|1"));
        Assert.True(ex.Expanded);
        Assert.Equal(42L, ex.EntryId);
        Assert.True(ex.IsDone);

        var co = Assert.IsType<CallbackData.ConvertView>(CallbackData.Parse("cv|c|r|usd|5000|nbu|3|0|0"));
        Assert.False(co.Expanded);
        Assert.False(co.IsDone);
    }

    [Fact]
    public void Parse_ConvertMark_ExtractsAllFields()
    {
        var parsed = Assert.IsType<CallbackData.ConvertMark>(CallbackData.Parse("cmk|99|f|eur|100|monobank|7.7|c"));
        Assert.Equal(99L, parsed.EntryId);
        Assert.Equal('f', parsed.Direction);
        Assert.Equal("eur", parsed.Foreign);
        Assert.Equal(100m, parsed.Amount);
        Assert.Equal("monobank", parsed.Bank);
        Assert.Equal(7.7m, parsed.Margin);
        Assert.False(parsed.Expanded);
    }

    [Fact]
    public void Parse_Garbage_ReturnsUnknown()
    {
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("garbage"));
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse(""));
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("cb|x|y"));
    }

    [Fact]
    public void Parse_TruncatedConvertCallback_ReturnsUnknown()
    {
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("cb|f|eur"));
    }

    [Fact]
    public void Parse_TableModify_ExtractsCurrencyAndBank()
    {
        var parsed = Assert.IsType<CallbackData.TableModify>(CallbackData.Parse("tt|eur|monobank"));
        Assert.Equal("eur", parsed.Currency);
        Assert.Equal("monobank", parsed.Bank);
    }

    [Fact]
    public void Parse_ScenarioModify_ExtractsCurrencyAndBank()
    {
        var parsed = Assert.IsType<CallbackData.ScenarioModify>(CallbackData.Parse("ts|usd|nbu"));
        Assert.Equal("usd", parsed.Currency);
        Assert.Equal("nbu", parsed.Bank);
    }


    [Fact]
    public void Parse_HistoryPage_ExtractsOffset()
    {
        var parsed = Assert.IsType<CallbackData.HistoryPage>(CallbackData.Parse("hp|10"));
        Assert.Equal(10, parsed.Offset);
    }

    [Fact]
    public void Parse_HistoryPage_NonNumericOffset_ReturnsUnknown()
    {
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("hp|abc"));
    }

    [Fact]
    public void Parse_HistoryClear()
    {
        Assert.IsType<CallbackData.HistoryClear>(CallbackData.Parse("hc"));
    }

    [Fact]
    public void Parse_HistoryToggle_OnAndOff()
    {
        var on = Assert.IsType<CallbackData.HistoryToggle>(CallbackData.Parse("ht|on"));
        Assert.True(on.Enabled);

        var off = Assert.IsType<CallbackData.HistoryToggle>(CallbackData.Parse("ht|off"));
        Assert.False(off.Enabled);
    }

    [Fact]
    public void Parse_BestHintToggle_OnAndOff()
    {
        var on = Assert.IsType<CallbackData.BestHintToggle>(CallbackData.Parse("bh|on"));
        Assert.True(on.Enabled);

        var off = Assert.IsType<CallbackData.BestHintToggle>(CallbackData.Parse("bh|off"));
        Assert.False(off.Enabled);
    }

    [Fact]
    public void Parse_HistoryStatsOpen_WithPeriod()
    {
        var all = Assert.IsType<CallbackData.HistoryStatsOpen>(CallbackData.Parse("hs|all"));
        Assert.Equal(StatsPeriod.All, all.Period);

        var today = Assert.IsType<CallbackData.HistoryStatsOpen>(CallbackData.Parse("hs|td"));
        Assert.Equal(StatsPeriod.Today, today.Period);

        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("hs|garbage"));
    }

    [Fact]
    public void Parse_HistoryEntryToggle_ExtractsIdAndOffset()
    {
        var parsed = Assert.IsType<CallbackData.HistoryEntryToggle>(CallbackData.Parse("htg|42|10"));
        Assert.Equal(42L, parsed.EntryId);
        Assert.Equal(10, parsed.PageOffset);
    }

    [Fact]
    public void Parse_HistoryEntryToggle_NonNumericId_ReturnsUnknown()
    {
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("htg|abc|0"));
    }

    [Fact]
    public void Parse_NullRaw_ReturnsUnknown() =>
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse(null!));

    [Fact]
    public void Parse_OnlyDelimiters_ReturnsUnknown() =>
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("|||"));

    [Fact]
    public void Parse_TrailingDelimiter_ReturnsUnknown() =>
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("rr|"));

    [Fact]
    public void Parse_VeryLongRaw_StillProducesUnknown()
    {
        var noisy = "cb|f|" + new string('x', 200);
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse(noisy));
    }

    [Fact]
    public void Parse_HistoryStatsOpen_TrailingNoise_ReturnsUnknown() =>
        Assert.IsType<CallbackData.Unknown>(CallbackData.Parse("hs|all|extra"));

    [Fact]
    public void Parse_NegativeOffsetForHistoryPage_StillParsedAsValue()
    {
        var parsed = Assert.IsType<CallbackData.HistoryPage>(CallbackData.Parse("hp|-5"));
        Assert.Equal(-5, parsed.Offset);
    }

    [Fact]
    public void Parse_StatsPeriod_AllValues_Roundtrip()
    {
        foreach (var period in new[] { StatsPeriod.Today, StatsPeriod.Week, StatsPeriod.Month, StatsPeriod.All })
        {
            var parsed = Assert.IsType<CallbackData.HistoryStatsOpen>(CallbackData.Parse($"hs|{period}"));
            Assert.Equal(period, parsed.Period);
        }
    }
}
