using DailyNewsBot.Processing;
using Xunit;

namespace DailyNewsBot.Tests;

public class ChunkBuilderTests
{
    [Fact]
    public void BuildChunks_ShortText_ReturnsSingleChunk()
    {
        var text = "Kurzer Text";
        var chunks = ChunkBuilder.BuildChunks(text);
        Assert.Single(chunks);
        Assert.Equal("Kurzer Text", chunks[0]);
    }

    [Fact]
    public void BuildChunks_TextOver1900_SplitsIntoMultipleChunks()
    {
        var text = string.Concat(Enumerable.Repeat("a", 2000));
        var chunks = ChunkBuilder.BuildChunks(text);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 1900));
    }

    [Fact]
    public void BuildChunks_PrefersArticleBoundary()
    {
        // Text mit Artikel-Trenner nach ca. 1000 Zeichen, dann mehr Text bis >1900
        var part1 = string.Concat(Enumerable.Repeat("x", 950));
        var part2 = "\n🔹 **Zweiter Artikel**\n" + string.Concat(Enumerable.Repeat("y", 1000));
        var text = part1 + part2;

        var chunks = ChunkBuilder.BuildChunks(text);
        Assert.True(chunks.Count >= 2);
        // Zweiter Chunk soll mit Artikel-Marker beginnen
        Assert.StartsWith("🔹", chunks[1]);
    }

    [Fact]
    public void BuildChunks_NeverSplitsLink()
    {
        // Link der genau über die 1900-Zeichen-Grenze geht
        var before = string.Concat(Enumerable.Repeat("a", 1895));
        var link = "<https://example.com/sehr-langer-pfad>";
        var after = " Mehr Text hier";
        var text = before + link + after;

        var chunks = ChunkBuilder.BuildChunks(text);
        // Kein Chunk darf einen Link mitten drin haben
        foreach (var chunk in chunks)
        {
            var openAngles  = chunk.Count(c => c == '<');
            var closeAngles = chunk.Count(c => c == '>');
            // Jede öffnende spitze Klammer muss eine schließende haben
            Assert.Equal(openAngles, closeAngles);
        }
    }

    [Fact]
    public void BuildChunks_AllContentPreserved()
    {
        var text = "Teil A\n\n🔹 Artikel\nText hier\n<https://link.de>\n\n" +
                   string.Concat(Enumerable.Repeat("b", 1800));
        var chunks = ChunkBuilder.BuildChunks(text);
        var combined = string.Join("", chunks);
        // Alle originalen Wörter müssen erhalten sein (ohne Whitespace-Unterschiede)
        Assert.Contains("Teil A", combined);
        Assert.Contains("https://link.de", combined);
    }
}
