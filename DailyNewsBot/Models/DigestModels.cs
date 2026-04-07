namespace DailyNewsBot.Models;

// Repräsentiert eine Kategorie mit ihren Feeds für einen Digest-Lauf
public record CategoryData(string Label, string Emoji, List<FeedConfig> Feeds);

// Feed-Konfiguration (aus DB oder Default)
public record FeedConfig(string Name, string Url, int MaxItems);

// Ein verarbeiteter Artikel bereit zum Anzeigen
public record ProcessedArticle(string Title, string Url, string Summary, string UrlHash, string Source);
