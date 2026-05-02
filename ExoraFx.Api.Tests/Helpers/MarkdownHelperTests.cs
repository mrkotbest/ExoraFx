using ExoraFx.Api.Helpers;

namespace ExoraFx.Api.Tests.Helpers;

public class MarkdownHelperTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("under_score", "under\\_score")]
    [InlineData("*bold*", "\\*bold\\*")]
    [InlineData("`code`", "\\`code\\`")]
    [InlineData("[link]", "\\[link]")]
    [InlineData("a_b*c`d[e", "a\\_b\\*c\\`d\\[e")]
    public void Escape_AddsBackslashesBeforeReservedChars(string input, string expected) =>
        Assert.Equal(expected, MarkdownHelper.Escape(input));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("*bold*", "bold")]
    [InlineData("under_score", "underscore")]
    [InlineData("`code`", "code")]
    [InlineData("a_b*c`d", "abcd")]
    public void Strip_RemovesEmphasisChars(string input, string expected) =>
        Assert.Equal(expected, MarkdownHelper.Strip(input));
}
