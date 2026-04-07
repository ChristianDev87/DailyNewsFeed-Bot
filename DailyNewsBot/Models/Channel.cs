namespace DailyNewsBot.Models;

public class Channel
{
    public string ChannelId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string? GuildName { get; set; }
    public string? ChannelName { get; set; }
    public string OwnerUserId { get; set; } = "";
    public bool Active { get; set; }
    public string? CustomBotTokenEncrypted { get; set; }
    public string? CustomBotTokenIv { get; set; }
    public DateTime CreatedAt { get; set; }
}
