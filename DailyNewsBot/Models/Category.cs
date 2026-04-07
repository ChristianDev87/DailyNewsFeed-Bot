namespace DailyNewsBot.Models;

public class Category
{
    public int Id { get; set; }
    public string ChannelId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Emoji { get; set; } = "";
    public int Position { get; set; }
    public bool Active { get; set; }
    public bool UseDefault { get; set; }
}
