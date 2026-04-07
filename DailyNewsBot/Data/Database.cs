using MySqlConnector;

namespace DailyNewsBot.Data;

public class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration config)
    {
        var host    = config["DB_HOST"]          ?? "localhost";
        var port    = config["DB_PORT"]          ?? "3306";
        var name    = config["DB_NAME"]          ?? "daily_news";
        var user    = config["DB_USER"]          ?? throw new InvalidOperationException("DB_USER nicht gesetzt");
        var pass    = config["DB_PASS"]          ?? throw new InvalidOperationException("DB_PASS nicht gesetzt");
        var pool    = config["DB_MAX_POOL_SIZE"] ?? "20";

        _connectionString =
            $"Server={host};Port={port};Database={name};" +
            $"User={user};Password={pass};" +
            $"MaximumPoolSize={pool};AllowZeroDateTime=True;ConvertZeroDateTime=True;";
    }

    public MySqlConnection GetConnection() => new(_connectionString);

    public async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        return conn;
    }
}
