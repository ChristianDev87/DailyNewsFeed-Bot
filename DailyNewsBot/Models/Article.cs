namespace DailyNewsBot.Models;

public class Article
{
    public string UrlHash { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime SeenAt { get; set; }
}
