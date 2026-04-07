namespace DailyNewsBot.Models;

public class Feed
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int MaxItems { get; set; } = 5;
    public bool Active { get; set; }
}
