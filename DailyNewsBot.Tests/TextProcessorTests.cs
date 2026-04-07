using DailyNewsBot.Processing;
using Xunit;

namespace DailyNewsBot.Tests;

public class TextProcessorTests
{
    // Fix 1: HTML-Tags entfernen
    [Fact]
    public void ProcessSummary_RemovesHtmlTags()
    {
        var result = TextProcessor.ProcessSummary("<p>Hello <b>World</b></p>");
        Assert.Equal("Hello World", result);
    }

    // Fix 2: HTML-Entities dekodieren
    [Fact]
    public void ProcessSummary_DecodesHtmlEntities()
    {
        var result = TextProcessor.ProcessSummary("Titel &#8211; Untertitel &amp; mehr");
        Assert.Equal("Titel – Untertitel & mehr", result);
    }

    [Fact]
    public void ProcessSummary_DecodesSmartQuotes()
    {
        var result = TextProcessor.ProcessSummary("&#8220;Zitat&#8221;");
        Assert.Equal("\u201cZitat\u201d", result);
    }

    // Fix 3: Quellenhinweise entfernen
    [Fact]
    public void ProcessSummary_RemovesGermanSourceAttribution()
    {
        var result = TextProcessor.ProcessSummary(
            "Ein interessanter Artikel. Der Artikel XY erschien zuerst auf The Decoder.");
        Assert.Equal("Ein interessanter Artikel.", result);
    }

    [Fact]
    public void ProcessSummary_RemovesEnglishSourceAttribution()
    {
        var result = TextProcessor.ProcessSummary(
            "Great content. The post ABC appeared first on Example Blog.");
        Assert.Equal("Great content.", result);
    }

    [Fact]
    public void ProcessSummary_RemovesViaAttribution()
    {
        var result = TextProcessor.ProcessSummary("Some text (via Hacker News)");
        Assert.Equal("Some text", result);
    }

    // Fix 4 + 5: Kürzung und doppelte Auslassung
    [Fact]
    public void ProcessSummary_TruncatesAtSentenceEnd()
    {
        var longText = string.Concat(Enumerable.Repeat("Wort ", 120)); // >500 Zeichen
        var result = TextProcessor.ProcessSummary(longText);
        Assert.True(result.Length <= 510); // 500 + " …" Puffer
        Assert.EndsWith(" …", result);
    }

    [Fact]
    public void ProcessSummary_DoesNotAddEllipsisIfAlreadyTruncated()
    {
        var text = "Kurzer Text...";
        var result = TextProcessor.ProcessSummary(text);
        Assert.DoesNotContain("... …", result);
        Assert.DoesNotContain("… …", result);
    }

    [Fact]
    public void ProcessSummary_DoesNotAddEllipsisIfBracketed()
    {
        var text = "Kurzer Text [...]";
        var result = TextProcessor.ProcessSummary(text);
        Assert.DoesNotContain("[...] …", result);
    }

    [Fact]
    public void ProcessTitle_RemovesHtmlAndDecodes()
    {
        var result = TextProcessor.ProcessTitle("<b>Titel &amp; Untertitel</b>");
        Assert.Equal("Titel & Untertitel", result);
    }

    [Fact]
    public void ProcessSummary_NormalizesWhitespace()
    {
        var result = TextProcessor.ProcessSummary("Text   mit    vielen   Leerzeichen");
        Assert.Equal("Text mit vielen Leerzeichen", result);
    }
}
