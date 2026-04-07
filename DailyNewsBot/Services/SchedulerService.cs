using Dapper;
using DailyNewsBot.Data;

namespace DailyNewsBot.Services;

public class SchedulerService : BackgroundService
{
    private readonly DigestService _digestService;
    private readonly IBotClientProvider _clientProvider;
    private readonly Database _db;
    private readonly ILogger<SchedulerService> _logger;

    public DateTime NextRunTime { get; private set; }

    public SchedulerService(
        DigestService digestService,
        IBotClientProvider clientProvider,
        Database db,
        ILogger<SchedulerService> logger)
    {
        _digestService = digestService;
        _clientProvider = clientProvider;
        _db = db;
        _logger = logger;
        NextRunTime = GetNextRunTime();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SchedulerService gestartet. Nächster Lauf: {NextRun:HH:mm} UTC", NextRunTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunTime - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await TryRunDigestAsync(stoppingToken);

            NextRunTime = GetNextRunTime();
            _logger.LogInformation("Nächster Scheduler-Lauf: {NextRun:HH:mm} UTC", NextRunTime);
        }
    }

    private async Task TryRunDigestAsync(CancellationToken ct)
    {
        using var conn = _db.GetConnection();
        await conn.OpenAsync(ct);

        var lockResult = await conn.ExecuteScalarAsync<int>(
            "SELECT GET_LOCK('daily_news_scheduler', 0)");

        if (lockResult != 1)
        {
            _logger.LogInformation("Scheduler-Lock nicht erhalten — andere Instanz aktiv");
            return;
        }

        try
        {
            _logger.LogInformation("Scheduler-Lock erhalten — starte Digest-Lauf");
            await _digestService.RunAllChannelsAsync(_clientProvider, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler im Scheduler-Digest-Lauf");
        }
        finally
        {
            await conn.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK('daily_news_scheduler')");
            _logger.LogInformation("Scheduler-Lock freigegeben");
        }
    }

    private static DateTime GetNextRunTime()
    {
        var now = DateTime.UtcNow;
        var currentBlock = now.Hour - (now.Hour % 4);
        var next = now.Date.AddHours(currentBlock + 4);
        return next <= now ? next.AddHours(4) : next;
    }
}
