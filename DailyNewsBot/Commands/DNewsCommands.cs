using Dapper;
using DailyNewsBot.Data;
using DailyNewsBot.Services;
using Discord.WebSocket;

namespace DailyNewsBot.Commands;

public static class DNewsCommands
{
    public static async Task HandleAsync(
        SocketSlashCommand cmd,
        IBotClientProvider clientProvider,
        Database db,
        DigestService digestService,
        string dashboardUrl)
    {
        var subCommand = cmd.Data.Options.FirstOrDefault()?.Name ?? "";

        switch (subCommand)
        {
            case "setup":     await HandleSetupAsync(cmd, db, dashboardUrl);                          break;
            case "senden":    await HandleSendenAsync(cmd, clientProvider, db, digestService, dashboardUrl); break;
            case "status":    await HandleStatusAsync(cmd, db);                                      break;
            case "feeds":     await HandleFeedsAsync(cmd, db, dashboardUrl);                          break;
            case "pause":     await HandlePauseAsync(cmd, db);                                       break;
            case "fortsetzen":await HandleFortsetzenAsync(cmd, db);                                  break;
            default:
                await cmd.RespondAsync("❌ Unbekannter Befehl.", ephemeral: true);
                break;
        }
    }

    // /dnews setup
    private static async Task HandleSetupAsync(
        SocketSlashCommand cmd, Database db, string dashboardUrl)
    {
        if (!IsAdmin(cmd))
        {
            await cmd.RespondAsync("❌ Nur Server-Admins können diesen Befehl nutzen.", ephemeral: true);
            return;
        }

        var channelId = cmd.Channel.Id.ToString();
        var guildId   = (cmd.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "";
        var guildName = (cmd.Channel as SocketGuildChannel)?.Guild.Name ?? "";

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var existing = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM channels WHERE channel_id = @channelId",
            new { channelId });

        if (existing > 0)
        {
            await cmd.RespondAsync(
                $"ℹ️ Dieser Kanal ist bereits eingerichtet.\n" +
                $"Konfiguration anpassen: {dashboardUrl}",
                ephemeral: true);
            return;
        }

        await conn.ExecuteAsync(
            "INSERT INTO channels (channel_id, guild_id, guild_name, channel_name, owner_user_id, active, created_at) " +
            "VALUES (@channelId, @guildId, @guildName, @channelName, @ownerUserId, 1, NOW())",
            new
            {
                channelId,
                guildId,
                guildName,
                channelName = cmd.Channel.Name,
                ownerUserId = cmd.User.Id.ToString(),
            });

        await cmd.RespondAsync(
            $"✅ Kanal wurde eingerichtet!\n" +
            $"Bitte konfiguriere jetzt Feeds und Kategorien im Dashboard:\n" +
            $"{dashboardUrl}");
    }

    // /dnews senden
    private static async Task HandleSendenAsync(
        SocketSlashCommand cmd,
        IBotClientProvider clientProvider,
        Database db,
        DigestService digestService,
        string dashboardUrl)
    {
        if (!IsAdmin(cmd))
        {
            await cmd.RespondAsync("❌ Nur Server-Admins können diesen Befehl nutzen.", ephemeral: true);
            return;
        }

        var channelId = cmd.Channel.Id.ToString();

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var active = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM channels WHERE channel_id = @channelId AND active = 1",
            new { channelId });

        if (active == 0)
        {
            await cmd.RespondAsync(
                $"❌ Dieser Kanal ist nicht konfiguriert.\n" +
                $"Bitte richte ihn zuerst ein: `/dnews setup`\n" +
                $"Oder im Dashboard: {dashboardUrl}",
                ephemeral: true);
            return;
        }

        var feedCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(cf.id) FROM channel_feeds cf " +
            "JOIN channel_categories cc ON cc.id = cf.category_id " +
            "WHERE cc.channel_id = @channelId AND cf.active = 1",
            new { channelId });

        if (feedCount == 0)
        {
            await cmd.RespondAsync(
                $"⚠️ Keine Feeds konfiguriert.\nBitte richte Feeds im Dashboard ein: {dashboardUrl}",
                ephemeral: true);
            return;
        }

        await cmd.DeferAsync();

        try
        {
            await digestService.RunSingleChannelAsync(channelId, clientProvider, CancellationToken.None);
            await cmd.FollowupAsync("✅ Nachrichtenüberblick wurde gesendet.");
        }
        catch (Exception ex)
        {
            await cmd.FollowupAsync($"❌ Fehler beim Senden: {ex.Message}");
        }
    }

    // /dnews status
    private static async Task HandleStatusAsync(SocketSlashCommand cmd, Database db)
    {
        var channelId = cmd.Channel.Id.ToString();

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var lastRun = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX(seen_at) FROM seen_articles WHERE channel_id = @channelId",
            new { channelId });

        var articlesToday = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM seen_articles WHERE channel_id = @channelId AND DATE(seen_at) = CURDATE()",
            new { channelId });

        var feedCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(cf.id) FROM channel_feeds cf " +
            "JOIN channel_categories cc ON cc.id = cf.category_id " +
            "WHERE cc.channel_id = @channelId AND cf.active = 1",
            new { channelId });

        var lastRunStr = lastRun.HasValue
            ? lastRun.Value.ToString("dd.MM.yyyy HH:mm") + " UTC"
            : "Noch kein Lauf";

        await cmd.RespondAsync(
            $"📊 **Daily News — Status**\n" +
            $"Letzter Lauf: `{lastRunStr}`\n" +
            $"Artikel heute: `{articlesToday}`\n" +
            $"Aktive Feeds: `{feedCount}`",
            ephemeral: true);
    }

    // /dnews feeds
    private static async Task HandleFeedsAsync(SocketSlashCommand cmd, Database db, string dashboardUrl)
    {
        if (!IsAdmin(cmd))
        {
            await cmd.RespondAsync("❌ Nur Server-Admins können diesen Befehl nutzen.", ephemeral: true);
            return;
        }

        var channelId = cmd.Channel.Id.ToString();

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var categories = (await conn.QueryAsync(
            "SELECT cc.label, cc.emoji, cf.name, cf.url " +
            "FROM channel_categories cc " +
            "JOIN channel_feeds cf ON cf.category_id = cc.id " +
            "WHERE cc.channel_id = @channelId AND cc.active = 1 AND cf.active = 1 " +
            "ORDER BY cc.position, cf.id",
            new { channelId })).ToList();

        if (!categories.Any())
        {
            await cmd.RespondAsync(
                $"⚠️ Keine Feeds konfiguriert.\nBitte richte Feeds im Dashboard ein: {dashboardUrl}",
                ephemeral: true);
            return;
        }

        var lines = categories
            .GroupBy(c => $"{c.emoji} {c.label}")
            .Select(g => $"**{g.Key}**\n" + string.Join("\n", g.Select(f => $"  • {f.name}")));

        await cmd.RespondAsync(
            "📡 **Aktive Feeds:**\n\n" + string.Join("\n\n", lines),
            ephemeral: true);
    }

    // /dnews pause
    private static async Task HandlePauseAsync(SocketSlashCommand cmd, Database db)
    {
        if (!IsAdmin(cmd))
        {
            await cmd.RespondAsync("❌ Nur Server-Admins können diesen Befehl nutzen.", ephemeral: true);
            return;
        }

        var channelId = cmd.Channel.Id.ToString();
        using var conn = db.GetConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "UPDATE channels SET active = 0 WHERE channel_id = @channelId",
            new { channelId });

        await cmd.RespondAsync("⏸️ Automatischer Nachrichtenüberblick pausiert.");
    }

    // /dnews fortsetzen
    private static async Task HandleFortsetzenAsync(SocketSlashCommand cmd, Database db)
    {
        if (!IsAdmin(cmd))
        {
            await cmd.RespondAsync("❌ Nur Server-Admins können diesen Befehl nutzen.", ephemeral: true);
            return;
        }

        var channelId = cmd.Channel.Id.ToString();
        using var conn = db.GetConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "UPDATE channels SET active = 1 WHERE channel_id = @channelId",
            new { channelId });

        await cmd.RespondAsync("▶️ Automatischer Nachrichtenüberblick wird fortgesetzt.");
    }

    private static bool IsAdmin(SocketSlashCommand cmd)
    {
        var guildUser = cmd.User as SocketGuildUser;
        return guildUser?.GuildPermissions.Administrator ?? false;
    }
}
