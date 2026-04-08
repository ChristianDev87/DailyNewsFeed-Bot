using Dapper;
using DailyNewsBot.Data;

namespace DailyNewsBot.Services;

public class HeartbeatService : BackgroundService
{
    private readonly Database _db;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(Database db, ILogger<HeartbeatService> logger)
    {
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var conn = _db.GetConnection();
                await conn.OpenAsync(stoppingToken);
                await conn.ExecuteAsync(
                    "INSERT INTO bot_status (id, last_seen) VALUES (1, UTC_TIMESTAMP()) " +
                    "ON DUPLICATE KEY UPDATE last_seen = UTC_TIMESTAMP()");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat-Schreiben fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
