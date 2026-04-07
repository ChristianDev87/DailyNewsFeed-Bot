using Dapper;
using DailyNewsBot.Data;
using DailyNewsBot.Models;
using DailyNewsBot.Processing;
using Discord;
using Discord.Rest;
using System.Text;

namespace DailyNewsBot.Services;

public class DigestService
{
    private readonly Database _db;
    private readonly FeedFetcher _feedFetcher;
    private readonly ILogger<DigestService> _logger;
    private readonly int _maxParallelFeeds;

    // Standard-Fallback-Kategorien (wenn Kanal nicht in DB oder keine Kategorien konfiguriert)
    private static readonly List<CategoryData> DefaultCategories =
    [
        new("🤖 KI & Künstliche Intelligenz", "🤖",
        [
            new("The Decoder (DE)",  "https://the-decoder.de/feed/",                           5),
            new("IT Boltwise",       "https://it-boltwise.de/feed",                            4),
            new("Heise KI",          "https://www.heise.de/thema/Kuenstliche-Intelligenz.xml",  5),
        ]),
        new("💻 IT & Hardware", "💻",
        [
            new("Heise Online",      "https://www.heise.de/rss/heise-atom.xml",                 5),
            new("Golem",             "https://rss.golem.de/rss.php?feed=RSS2.0",                4),
            new("PC Games Hardware", "https://www.pcgameshardware.de/feed.cfm?menu_alias=home", 4),
            new("Tom's Hardware",    "https://www.tomshardware.com/feeds/all",                  4),
        ]),
        new("🛠️ Dev & IT-Technik", "🛠️",
        [
            new("Heise Developer",   "https://www.heise.de/developer/rss/news-atom.xml",        4),
            new("Heise Security",    "https://www.heise.de/security/rss/news-atom.xml",         3),
            new("c't Magazin",       "https://www.heise.de/ct/rss/artikel-atom.xml",            3),
            new("iX Magazin",        "https://www.heise.de/ix/feed.xml",                        3),
            new("Hacker News",       "https://hnrss.org/frontpage",                             5),
            new("t3n",               "https://t3n.de/rss.xml",                                  3),
            new(".NET Blog",         "https://devblogs.microsoft.com/dotnet/feed/",             2),
        ]),
        new("🎮 Gaming", "🎮",
        [
            new("GameStar",          "https://www.gamestar.de/news/rss/news.rss",               5),
            new("PC Games",          "https://www.pcgames.de/feed/news/",                       4),
            new("Eurogamer DE",      "https://www.eurogamer.de/feed",                           4),
            new("Golem Games",       "https://rss.golem.de/rss.php?tp=games&feed=RSS2.0",       3),
            new("IGN",               "https://feeds.feedburner.com/ign/games-all",              3),
            new("Rock Paper Shotgun","https://www.rockpapershotgun.com/feed",                   3),
        ]),
    ];

    public DigestService(
        Database db,
        FeedFetcher feedFetcher,
        ILogger<DigestService> logger,
        IConfiguration config)
    {
        _db = db;
        _feedFetcher = feedFetcher;
        _logger = logger;
        _maxParallelFeeds = int.TryParse(config["MAX_PARALLEL_FEEDS"], out var n) ? n : 10;
    }

    /// <summary>
    /// Führt den Digest für alle aktiven Kanäle aus.
    /// Ein fehlgeschlagener Kanal stoppt nicht die anderen.
    /// </summary>
    public async Task RunAllChannelsAsync(IBotClientProvider clientProvider, CancellationToken ct)
    {
        var channels = await GetActiveChannelsAsync(ct);
        _logger.LogInformation("Digest-Lauf für {Count} aktive Kanäle", channels.Count);

        for (int i = 0; i < channels.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await RunSingleChannelAsync(channels[i].ChannelId, clientProvider, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Digest fehlgeschlagen für Kanal {ChannelId}", channels[i].ChannelId);
            }

            if (i < channels.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    /// <summary>
    /// Führt den Digest für einen einzelnen Kanal aus.
    /// </summary>
    public async Task RunSingleChannelAsync(
        string channelId,
        IBotClientProvider clientProvider,
        CancellationToken ct)
    {
        // Pessimistischer Lock auf Kanal-Ebene
        await using var conn = await _db.GetOpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var channel = await conn.QueryFirstOrDefaultAsync<Channel>(
            "SELECT * FROM channels WHERE channel_id = @channelId AND active = 1 FOR UPDATE",
            new { channelId }, tx);

        if (channel is null)
        {
            _logger.LogWarning("Kanal {ChannelId} nicht aktiv oder nicht gefunden", channelId);
            await tx.RollbackAsync(ct);
            return;
        }

        // Bereits gesehene Artikel laden
        var seenHashes = (await conn.QueryAsync<string>(
            "SELECT url_hash FROM seen_articles WHERE channel_id = @channelId",
            new { channelId }, tx)).ToHashSet();

        await tx.CommitAsync(ct);

        // Kategorien ermitteln (eigene oder Standard-Fallback)
        var categories = await GetCategoriesForChannelAsync(channelId, ct);

        // Feeds parallel holen
        var newArticlesByCategory = new List<(CategoryData Category, List<ProcessedArticle> Articles)>();
        var seenHashesLock = new object();
        using var sem = new SemaphoreSlim(_maxParallelFeeds);

        var tasks = categories.Select(async cat =>
        {
            var catArticles = new List<ProcessedArticle>();
            foreach (var feed in cat.Feeds)
            {
                await sem.WaitAsync(ct);
                try
                {
                    HashSet<string> snapshot;
                    lock (seenHashesLock) { snapshot = [..seenHashes]; }

                    var articles = await _feedFetcher.FetchArticlesAsync(feed, channelId, snapshot, ct);
                    catArticles.AddRange(articles);

                    lock (seenHashesLock)
                    {
                        foreach (var a in articles) seenHashes.Add(a.UrlHash);
                    }
                }
                finally { sem.Release(); }
            }
            return (cat, catArticles);
        });

        var results = await Task.WhenAll(tasks);

        var allNew = results.Where(r => r.catArticles.Any()).ToList();

        // Thread holen oder erstellen
        var restClient = clientProvider.GetRestClientForChannel(channelId);
        var threadId = await GetOrCreateThreadAsync(channelId, restClient, ct);

        if (threadId == 0)
        {
            _logger.LogError("Thread für Kanal {ChannelId} konnte nicht erstellt werden", channelId);
            return;
        }

        // Nachricht aufbauen und senden
        var text = BuildDigestText(allNew);
        var chunks = ChunkBuilder.BuildChunks(text);

        var threadChannel = await restClient.GetChannelAsync(threadId) as ITextChannel;
        if (threadChannel is null)
        {
            _logger.LogError("Thread {ThreadId} nicht erreichbar", threadId);
            return;
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            await SendWithRateLimitAsync(threadChannel, chunks[i], ct);
            if (i < chunks.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        // Neue Artikel in seen_articles speichern (Bulk)
        var newArticles = results.SelectMany(r => r.catArticles).ToList();
        if (newArticles.Any())
            await BulkInsertSeenArticlesAsync(newArticles, channelId);

        _logger.LogInformation(
            "Digest für Kanal {ChannelId}: {Count} neue Artikel gesendet",
            channelId, newArticles.Count);
    }

    private string BuildDigestText(
        List<(CategoryData Category, List<ProcessedArticle> Articles)> articlesByCategory)
    {
        if (!articlesByCategory.Any())
        {
            return $"🔄 **Update — {DateTime.Now:HH:mm} Uhr**\n" +
                   "✅ Keine neuen Artikel seit dem letzten Durchlauf.";
        }

        var sb = new StringBuilder();
        var isFirstOfDay = IsFirstRunToday();

        if (isFirstOfDay)
            sb.AppendLine($"📰 **News-Digest — {DateTime.Now:dd.MM.yyyy}**");
        else
            sb.AppendLine($"🔄 **Update — {DateTime.Now:HH:mm} Uhr**");

        foreach (var (category, articles) in articlesByCategory)
        {
            sb.AppendLine();
            sb.AppendLine($"{category.Emoji} {category.Label}");
            sb.AppendLine("────────────────────────────────");
            sb.AppendLine();

            foreach (var article in articles)
            {
                sb.AppendLine($"🔹 **{article.Title}**");
                if (!string.IsNullOrWhiteSpace(article.Summary))
                    sb.AppendLine(article.Summary);
                sb.AppendLine($"<{article.Url}>");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private bool _firstRunChecked;
    private bool _isFirstRun;

    private bool IsFirstRunToday()
    {
        // Vereinfachte Prüfung: ist es der erste Lauf (00:00-04:00)?
        // Eine robustere Implementierung könnte daily_threads prüfen
        var hour = DateTime.Now.Hour;
        return hour is >= 0 and < 4;
    }

    private async Task<ulong> GetOrCreateThreadAsync(
        string channelId, DiscordRestClient restClient, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Bestehenden Thread suchen
        using var conn = _db.GetConnection();
        await conn.OpenAsync(ct);

        var existingThreadId = await conn.ExecuteScalarAsync<string?>(
            "SELECT thread_id FROM daily_threads WHERE date = @date AND channel_id = @channelId",
            new { date = today, channelId });

        if (existingThreadId is not null && ulong.TryParse(existingThreadId, out var tid))
            return tid;

        // Neuen Thread erstellen
        if (!ulong.TryParse(channelId, out var chanId))
        {
            _logger.LogError("Ungültige channel_id: {ChannelId}", channelId);
            return 0;
        }

        var textChannel = await restClient.GetChannelAsync(chanId) as ITextChannel;
        if (textChannel is null)
        {
            _logger.LogError("Kanal {ChannelId} nicht gefunden oder kein Text-Kanal", channelId);
            return 0;
        }

        var thread = await textChannel.CreateThreadAsync(
            name: $"🔔 Daily News — {DateTime.Now:dd.MM.yyyy}",
            autoArchiveDuration: ThreadArchiveDuration.OneWeek,
            type: ThreadType.PublicThread);

        // Thread-ID speichern
        await conn.ExecuteAsync(
            "INSERT IGNORE INTO daily_threads (date, channel_id, thread_id, created_at) " +
            "VALUES (@date, @channelId, @threadId, NOW())",
            new { date = today, channelId, threadId = thread.Id.ToString() });

        return thread.Id;
    }

    private async Task SendWithRateLimitAsync(
        ITextChannel channel, string content, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await channel.SendMessageAsync(content);
                return;
            }
            catch (Discord.Net.HttpException ex) when ((int)ex.HttpCode == 429)
            {
                // Discord.Net IRequest does not expose response headers directly;
                // fall back to a conservative fixed delay.
                const double delay = 5.0;
                _logger.LogWarning("Discord Rate Limit — warte {Delay}s (Reason: {Reason})", delay, ex.Reason);
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }
    }

    private async Task<List<Channel>> GetActiveChannelsAsync(CancellationToken ct = default)
    {
        using var conn = _db.GetConnection();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<Channel>(
            "SELECT * FROM channels WHERE active = 1")).ToList();
    }

    private async Task<List<CategoryData>> GetCategoriesForChannelAsync(string channelId, CancellationToken ct = default)
    {
        using var conn = _db.GetConnection();
        await conn.OpenAsync(ct);

        var categories = (await conn.QueryAsync<Category>(
            "SELECT * FROM channel_categories WHERE channel_id = @channelId AND active = 1 ORDER BY position",
            new { channelId })).ToList();

        if (!categories.Any())
            return DefaultCategories;

        var result = new List<CategoryData>();
        foreach (var cat in categories)
        {
            if (cat.UseDefault)
            {
                var defaultCat = DefaultCategories.FirstOrDefault(d =>
                    d.Label.Contains(cat.Emoji, StringComparison.OrdinalIgnoreCase) ||
                    d.Label == cat.Label);
                if (defaultCat is not null) result.Add(defaultCat);
            }
            else
            {
                var feeds = (await conn.QueryAsync<Feed>(
                    "SELECT * FROM channel_feeds WHERE category_id = @id AND active = 1",
                    new { id = cat.Id })).ToList();
                result.Add(new CategoryData(
                    cat.Label, cat.Emoji,
                    feeds.Select(f => new FeedConfig(f.Name, f.Url, f.MaxItems)).ToList()));
            }
        }

        return result;
    }

    private async Task BulkInsertSeenArticlesAsync(List<ProcessedArticle> articles, string channelId)
    {
        if (!articles.Any()) return;

        using var conn = _db.GetConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "INSERT IGNORE INTO seen_articles (url_hash, channel_id, url, title, source, seen_at) " +
            "VALUES (@UrlHash, @ChannelId, @Url, @Title, @Source, NOW())",
            articles.Select(a => new
            {
                a.UrlHash,
                ChannelId = channelId,
                a.Url,
                a.Title,
                a.Source,
            }));
    }
}
