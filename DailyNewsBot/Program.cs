using DailyNewsBot.Data;
using DailyNewsBot.Processing;
using DailyNewsBot.Services;
using DotNetEnv;
using Serilog;
using Serilog.Formatting.Compact;

// .env suchen: vom aktuellen Verzeichnis aufwärts bis zur Wurzel
var searchDir = new DirectoryInfo(Directory.GetCurrentDirectory());
string? envPath = null;
while (searchDir != null)
{
    var candidate = Path.Combine(searchDir.FullName, ".env");
    if (File.Exists(candidate)) { envPath = candidate; break; }
    searchDir = searchDir.Parent;
}
if (envPath != null)
    Env.Load(envPath);
// Kein .env gefunden — Umgebungsvariablen müssen direkt gesetzt sein

const string consoleTemplate =
    "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext:l}: {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Discord", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: consoleTemplate)
    .WriteTo.File(new CompactJsonFormatter(), "logs/bot-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateBootstrapLogger();

try
{
    Log.Information("Daily News Bot startet...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((ctx, services, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .MinimumLevel.Override("Discord", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: consoleTemplate)
            .WriteTo.File(new CompactJsonFormatter(), "logs/bot-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14))
        .ConfigureAppConfiguration(cfg =>
        {
            cfg.AddEnvironmentVariables();
            cfg.AddJsonFile("appsettings.json", optional: true);
        })
        .ConfigureServices((ctx, services) =>
        {
            // Core Services
            services.AddSingleton<Database>();
            services.AddHttpClient("feeds", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("User-Agent", "DailyNewsBot/1.0");
            });
            services.AddSingleton<FeedFetcher>();
            services.AddSingleton<DigestService>();

            // Bot + Scheduler
            services.AddSingleton<BotService>();
            services.AddHostedService(sp => sp.GetRequiredService<BotService>());
            services.AddSingleton<IBotClientProvider>(sp => sp.GetRequiredService<BotService>());
            services.AddHostedService<SchedulerService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Bot konnte nicht gestartet werden");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
