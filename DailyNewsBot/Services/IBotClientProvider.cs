using Discord.Rest;

namespace DailyNewsBot.Services;

public interface IBotClientProvider
{
    DiscordRestClient GetRestClientForChannel(string channelId);
    Discord.WebSocket.DiscordSocketClient SocketClient { get; }
}
