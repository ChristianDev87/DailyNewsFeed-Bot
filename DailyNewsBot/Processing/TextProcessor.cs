using System.Net;
using System.Text.RegularExpressions;

namespace DailyNewsBot.Processing;

public static class TextProcessor
{
    private static readonly Regex HtmlTagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex =
        new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex AlreadyTruncatedRegex =
        new(@"(\[\.{3}\]|\.{3}|…|\[…\])\s*$", RegexOptions.Compiled);

    private static readonly Regex[] SourceAttributionPatterns =
    [
        new(@"\s*Der Artikel .{0,200} erschien zuerst auf [^.]+\.?\s*(\.{3}|…|\[\.{3}\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new(@"\s*This article (first )?appeared on [^.]+\.?\s*(\.{3}|…|\[\.{3}\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new(@"\s*Read (the full article|more) (at|on) [^.]+\.?\s*(\.{3}|…|\[\.{3}\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new(@"\s*The post .{0,200} appeared first on [^.]+\.?\s*(\.{3}|…|\[\.{3}\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new(@"\s*Weiterlesen (bei|auf) [^.]+\.?\s*(\.{3}|…|\[\.{3}\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new(@"\s*\(via [^)]+\)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Verarbeitet eine Feed-Summary: HTML entfernen, Entities dekodieren,
    /// Quellenhinweise entfernen, kürzen, Auslassung anhängen.
    /// </summary>
    public static string ProcessSummary(string raw, int maxChars = 500)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // Schritt 1: HTML-Tags entfernen
        var text = HtmlTagRegex.Replace(raw, " ");

        // Schritt 2: HTML-Entities dekodieren
        text = WebUtility.HtmlDecode(text);

        // Schritt 3: Whitespace normalisieren
        text = WhitespaceRegex.Replace(text, " ").Trim();

        // Schritt 4: Quellenhinweise entfernen
        foreach (var pattern in SourceAttributionPatterns)
            text = pattern.Replace(text, "");
        text = text.Trim();

        if (string.IsNullOrEmpty(text)) return "";

        // Schritt 5 + 6: Kürzen und Auslassung
        return Truncate(text, maxChars);
    }

    /// <summary>
    /// Verarbeitet einen Artikel-Titel.
    /// </summary>
    public static string ProcessTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = HtmlTagRegex.Replace(raw, " ");
        text = WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            // Kein Kürzen nötig — aber Auslassung prüfen
            // Keine Auslassung wenn Text kurz und vollständig
            return text;
        }

        var window = text[..maxChars];

        // Bevorzugt am Satzende kürzen
        foreach (var sep in new[] { ". ", "! ", "? " })
        {
            var pos = window.LastIndexOf(sep, StringComparison.Ordinal);
            if (pos > maxChars / 2)
            {
                var cut = window[..(pos + 1)];
                return AlreadyTruncatedRegex.IsMatch(cut) ? cut.TrimEnd() : cut.TrimEnd() + " …";
            }
        }

        // Fallback: letztes Leerzeichen
        var spacePos = window.LastIndexOf(' ');
        if (spacePos > maxChars / 2)
        {
            var cut = window[..spacePos];
            return AlreadyTruncatedRegex.IsMatch(cut) ? cut.TrimEnd() : cut.TrimEnd() + " …";
        }

        // Absoluter Fallback
        return window.TrimEnd() + " …";
    }
}
