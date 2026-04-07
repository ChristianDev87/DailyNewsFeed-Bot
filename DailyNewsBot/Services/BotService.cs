using DailyNewsBot.Commands;
using Dapper;
using DailyNewsBot.Data;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Security.Cryptography;
using System.Text;

namespace DailyNewsBot.Services;

public class BotService : BackgroundService, IBotClientProvider
{
    private readonly Database _db;
    private readonly ILogger<BotService> _logger;
    private readonly string _botToken;
    private readonly string _masterKey;
    private readonly string _dashboardUrl;
    private readonly DigestService _digestService;

    private DiscordSocketClient _socketClient = null!;
    private DiscordRestClient _standardRestClient = null!;
    private readonly Dictionary<string, DiscordRestClient> _customClients = new();

    public DiscordSocketClient SocketClient => _socketClient;

    public BotService(
        Database db,
        DigestService digestService,
        ILogger<BotService> logger,
        IConfiguration config)
    {
        _db = db;
        _digestService = digestService;
        _logger = logger;
        _botToken     = config["DISCORD_BOT_TOKEN"]    ?? throw new InvalidOperationException("DISCORD_BOT_TOKEN nicht gesetzt");
        _masterKey    = config["TOKEN_ENCRYPTION_KEY"] ?? throw new InvalidOperationException("TOKEN_ENCRYPTION_KEY nicht gesetzt");
        _dashboardUrl = config["DASHBOARD_URL"]        ?? "https://your-dashboard.example.com";
    }

    public DiscordRestClient GetRestClientForChannel(string channelId)
    {
        return _customClients.TryGetValue(channelId, out var client)
            ? client
            : _standardRestClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            LogLevel = LogSeverity.Warning,
        };

        _socketClient = new DiscordSocketClient(config);
        _socketClient.Log           += LogAsync;
        _socketClient.Ready         += OnReadyAsync;
        _socketClient.JoinedGuild   += OnJoinedGuildAsync;
        _socketClient.LeftGuild     += OnLeftGuildAsync;
        _socketClient.SlashCommandExecuted += OnSlashCommandAsync;

        // Standard REST-Client
        _standardRestClient = new DiscordRestClient();
        await _standardRestClient.LoginAsync(TokenType.Bot, _botToken);

        await _socketClient.LoginAsync(TokenType.Bot, _botToken);
        await _socketClient.StartAsync();
        await _socketClient.SetCustomStatusAsync("Liest Nachrichtenfeeds...");

        await Task.Delay(Timeout.Infinite, stoppingToken);

        await _socketClient.StopAsync();
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Bot online als {Username}", _socketClient.CurrentUser.Username);

        // Custom-Token-Clients aufbauen
        await InitializeCustomClientsAsync();

        // Commands für alle bekannten Guilds registrieren
        using var conn = _db.GetConnection();
        await conn.OpenAsync();
        var guildIds = (await conn.QueryAsync<string>(
            "SELECT DISTINCT guild_id FROM channels")).ToList();

        foreach (var guildIdStr in guildIds)
        {
            if (ulong.TryParse(guildIdStr, out var guildId))
                await RegisterCommandsForGuildAsync(guildId);
        }
    }

    private async Task OnJoinedGuildAsync(SocketGuild guild)
    {
        _logger.LogInformation("Guild beigetreten: {GuildName} ({GuildId})", guild.Name, guild.Id);
        await RegisterCommandsForGuildAsync(guild.Id);
    }

    private async Task OnLeftGuildAsync(SocketGuild guild)
    {
        _logger.LogInformation("Guild verlassen: {GuildName} ({GuildId})", guild.Name, guild.Id);
        using var conn = _db.GetConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE channels SET active = 0 WHERE guild_id = @guildId",
            new { guildId = guild.Id.ToString() });
    }

    private async Task RegisterCommandsForGuildAsync(ulong guildId)
    {
        try
        {
            var command = new SlashCommandBuilder()
                .WithName("dnews")
                .WithDescription("Daily News Bot")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("setup")
                    .WithDescription("Richtet diesen Kanal für Daily News ein")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("senden")
                    .WithDescription("Sendet den aktuellen Nachrichtenüberblick sofort")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("status")
                    .WithDescription("Zeigt den Status des Nachrichtenüberblicks")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("feeds")
                    .WithDescription("Zeigt alle aktiven Nachrichtenquellen dieses Kanals")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("pause")
                    .WithDescription("Pausiert den automatischen Nachrichtenüberblick")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("fortsetzen")
                    .WithDescription("Setzt den automatischen Nachrichtenüberblick fort")
                    .WithType(ApplicationCommandOptionType.SubCommand))
                .Build();

            var guild = await _standardRestClient.GetGuildAsync(guildId);
            await guild.BulkOverwriteApplicationCommandsAsync([command]);
            _logger.LogInformation("Commands registriert für Guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command-Registrierung fehlgeschlagen für Guild {GuildId}", guildId);
        }
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand cmd)
    {
        if (cmd.CommandName != "dnews") return;
        await DNewsCommands.HandleAsync(cmd, this, _db, _digestService, _dashboardUrl);
    }

    private async Task InitializeCustomClientsAsync()
    {
        using var conn = _db.GetConnection();
        await conn.OpenAsync();

        var channels = (await conn.QueryAsync<Models.Channel>(
            "SELECT * FROM channels WHERE custom_bot_token_encrypted IS NOT NULL AND active = 1"))
            .ToList();

        foreach (var channel in channels)
        {
            try
            {
                var token = DecryptToken(channel.CustomBotTokenEncrypted!, channel.CustomBotTokenIv!);
                var client = new DiscordRestClient();
                await client.LoginAsync(TokenType.Bot, token);
                _customClients[channel.ChannelId] = client;
                _logger.LogInformation("Custom-Client für Kanal {ChannelId} initialisiert", channel.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Custom-Token für Kanal {ChannelId} ungültig — Fallback auf Standard-Token",
                    channel.ChannelId);
            }
        }
    }

    private string DecryptToken(string encryptedBase64, string ivBase64)
    {
        var key       = Convert.FromBase64String(_masterKey);
        var iv        = Convert.FromBase64String(ivBase64);
        var encrypted = Convert.FromBase64String(encryptedBase64);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV  = iv;

        var decrypted = aes.DecryptCbc(encrypted, iv);
        return Encoding.UTF8.GetString(decrypted);
    }

    private Task LogAsync(LogMessage msg)
    {
        if (msg.Exception is not null)
            _logger.LogWarning(msg.Exception, "Discord: {Message}", msg.Message);
        else
            _logger.LogDebug("Discord: {Message}", msg.Message);
        return Task.CompletedTask;
    }
}
