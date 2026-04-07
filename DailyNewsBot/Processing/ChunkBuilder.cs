using System.Text.RegularExpressions;

namespace DailyNewsBot.Processing;

public static class ChunkBuilder
{
    private const int MaxChunkSize = 1900;

    private static readonly Regex LinkRegex =
        new(@"<https?://[^\s>]+>", RegexOptions.Compiled);

    public static List<string> BuildChunks(string fullText)
    {
        var chunks = new List<string>();
        var remaining = fullText;

        while (remaining.Length > MaxChunkSize)
        {
            var cutPoint = FindCutPoint(remaining);
            chunks.Add(remaining[..cutPoint].TrimEnd());
            remaining = remaining[cutPoint..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
            chunks.Add(remaining.TrimEnd());

        return chunks;
    }

    private static int FindCutPoint(string text)
    {
        // Link-Schutz: niemals einen Link zerreißen
        foreach (Match match in LinkRegex.Matches(text))
        {
            if (match.Index < MaxChunkSize && match.Index + match.Length > MaxChunkSize)
            {
                if (match.Index > MaxChunkSize / 3)
                    return match.Index;
                break;
            }
        }

        var window = text[..MaxChunkSize];

        // Priorität 1: Vor neuem Artikel (🔹)
        var pos = window.LastIndexOf("\n🔹", StringComparison.Ordinal);
        if (pos > MaxChunkSize / 3) return pos;

        // Priorität 2: Vor Kategorie-Header
        foreach (var marker in new[] { "\n🤖", "\n💻", "\n🛠", "\n🎮" })
        {
            pos = window.LastIndexOf(marker, StringComparison.Ordinal);
            if (pos > MaxChunkSize / 3) return pos;
        }

        // Priorität 3: Doppelte Leerzeile
        pos = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (pos > MaxChunkSize / 3) return pos;

        // Priorität 4: Einfache Leerzeile
        pos = window.LastIndexOf('\n');
        if (pos > MaxChunkSize / 3) return pos;

        // Priorität 5: Satzende
        foreach (var sep in new[] { ". ", "! ", "? " })
        {
            pos = window.LastIndexOf(sep, StringComparison.Ordinal);
            if (pos > MaxChunkSize / 3) return pos + 1;
        }

        // Priorität 6: Leerzeichen (Notfall)
        pos = window.LastIndexOf(' ');
        if (pos > MaxChunkSize / 3) return pos;

        return MaxChunkSize;
    }
}
