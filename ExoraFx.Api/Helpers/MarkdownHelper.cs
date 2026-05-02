namespace ExoraFx.Api.Helpers;

public static class MarkdownHelper
{
    public static string Escape(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[");

    public static string Strip(string text) =>
        text.Replace("*", "").Replace("_", "").Replace("`", "");
}
