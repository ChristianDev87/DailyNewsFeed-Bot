using CodeHollow.FeedReader;
using DailyNewsBot.Models;
using Microsoft.Extensions.Http;

namespace DailyNewsBot.Processing;

public class FeedFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FeedFetcher> _logger;

    public FeedFetcher(IHttpClientFactory httpClientFactory, ILogger<FeedFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Holt Artikel aus einem RSS/Atom-Feed.
    /// Gibt leere Liste zurück wenn Feed nicht erreichbar oder ungültig.
    /// </summary>
    public async Task<List<ProcessedArticle>> FetchArticlesAsync(
        FeedConfig feedConfig,
        string channelId,
        HashSet<string> seenHashes,
        CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("feeds");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var content = await client.GetStringAsync(feedConfig.Url, cts.Token);
            var feed = FeedReader.ReadFromString(content);

            var articles = new List<ProcessedArticle>();
            var count = 0;

            foreach (var item in feed.Items)
            {
                if (count >= feedConfig.MaxItems) break;

                var url = item.Link ?? "";
                if (string.IsNullOrWhiteSpace(url)) continue;

                var urlHash = ComputeUrlHash(url);
                if (seenHashes.Contains(urlHash)) continue;

                var title   = TextProcessor.ProcessTitle(item.Title ?? "");
                var summary = TextProcessor.ProcessSummary(item.Description ?? item.Content ?? "");

                if (string.IsNullOrWhiteSpace(title)) continue;

                articles.Add(new ProcessedArticle(title, url, summary, urlHash, feedConfig.Name));
                count++;
            }

            return articles;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Feed-Timeout: {Url}", feedConfig.Url);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feed-Fehler: {Url}", feedConfig.Url);
            return [];
        }
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
