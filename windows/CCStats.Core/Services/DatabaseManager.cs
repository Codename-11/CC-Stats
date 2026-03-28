using CCStats.Core.Models;
using Microsoft.Data.Sqlite;

namespace CCStats.Core.Services;

public sealed class DatabaseManager : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private SqliteConnection? _connection;

    public DatabaseManager()
        : this(GetDefaultDatabasePath())
    {
    }

    public DatabaseManager(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={databasePath}";
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync();

        await ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS usage_polls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                five_hour_utilization REAL NOT NULL,
                five_hour_resets_at TEXT,
                seven_day_utilization REAL,
                seven_day_resets_at TEXT
            );

            CREATE TABLE IF NOT EXISTS usage_rollups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                resolution TEXT NOT NULL,
                avg_five_hour_utilization REAL NOT NULL,
                max_five_hour_utilization REAL NOT NULL,
                min_five_hour_utilization REAL NOT NULL,
                avg_seven_day_utilization REAL,
                max_seven_day_utilization REAL,
                min_seven_day_utilization REAL,
                sample_count INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                window_type TEXT NOT NULL,
                utilization_before REAL NOT NULL,
                utilization_after REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS outages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                duration_seconds REAL,
                error_type TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_usage_polls_timestamp ON usage_polls(timestamp);
            CREATE INDEX IF NOT EXISTS idx_usage_rollups_period ON usage_rollups(period_start, resolution);
            CREATE INDEX IF NOT EXISTS idx_reset_events_timestamp ON reset_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_outages_started ON outages(started_at);
            """);
    }

    public async Task<long> InsertPollAsync(UsagePoll poll)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usage_polls (timestamp, five_hour_utilization, five_hour_resets_at, seven_day_utilization, seven_day_resets_at)
                VALUES (@timestamp, @fiveHour, @fiveHourResets, @sevenDay, @sevenDayResets);
                SELECT last_insert_rowid();
                """;

            cmd.Parameters.AddWithValue("@timestamp", poll.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@fiveHour", poll.FiveHourUtilization);
            cmd.Parameters.AddWithValue("@fiveHourResets", poll.FiveHourResetsAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sevenDay", poll.SevenDayUtilization ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sevenDayResets", poll.SevenDayResetsAt?.ToString("O") ?? (object)DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<UsagePoll>> QueryPollsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                SELECT id, timestamp, five_hour_utilization, five_hour_resets_at, seven_day_utilization, seven_day_resets_at
                FROM usage_polls
                WHERE timestamp >= @from AND timestamp <= @to
                ORDER BY timestamp ASC;
                """;

            cmd.Parameters.AddWithValue("@from", from.ToString("O"));
            cmd.Parameters.AddWithValue("@to", to.ToString("O"));

            var polls = new List<UsagePoll>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                polls.Add(ReadPollFromReader(reader));
            }

            return polls;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<int> PruneOldDataAsync(int retentionDays)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");

            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                DELETE FROM usage_polls WHERE timestamp < @cutoff;
                DELETE FROM usage_rollups WHERE period_end < @cutoff;
                DELETE FROM reset_events WHERE timestamp < @cutoff;
                DELETE FROM outages WHERE started_at < @cutoff;
                """;
            cmd.Parameters.AddWithValue("@cutoff", cutoff);

            return await cmd.ExecuteNonQueryAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<long> GetDatabaseSizeAsync()
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        finally { _dbLock.Release(); }
    }

    public async Task InsertRollupAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string resolution,
        double avgFiveHour, double maxFiveHour, double minFiveHour,
        double? avgSevenDay, double? maxSevenDay, double? minSevenDay,
        int sampleCount)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usage_rollups (period_start, period_end, resolution, avg_five_hour_utilization, max_five_hour_utilization, min_five_hour_utilization, avg_seven_day_utilization, max_seven_day_utilization, min_seven_day_utilization, sample_count)
                VALUES (@periodStart, @periodEnd, @resolution, @avgFiveHour, @maxFiveHour, @minFiveHour, @avgSevenDay, @maxSevenDay, @minSevenDay, @sampleCount);
                """;

            cmd.Parameters.AddWithValue("@periodStart", periodStart.ToString("O"));
            cmd.Parameters.AddWithValue("@periodEnd", periodEnd.ToString("O"));
            cmd.Parameters.AddWithValue("@resolution", resolution);
            cmd.Parameters.AddWithValue("@avgFiveHour", avgFiveHour);
            cmd.Parameters.AddWithValue("@maxFiveHour", maxFiveHour);
            cmd.Parameters.AddWithValue("@minFiveHour", minFiveHour);
            cmd.Parameters.AddWithValue("@avgSevenDay", avgSevenDay ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@maxSevenDay", maxSevenDay ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@minSevenDay", minSevenDay ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sampleCount", sampleCount);

            await cmd.ExecuteNonQueryAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task InsertResetEventAsync(DateTimeOffset timestamp, string windowType, double utilizationBefore, double utilizationAfter)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO reset_events (timestamp, window_type, utilization_before, utilization_after)
                VALUES (@timestamp, @windowType, @before, @after);
                """;

            cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@windowType", windowType);
            cmd.Parameters.AddWithValue("@before", utilizationBefore);
            cmd.Parameters.AddWithValue("@after", utilizationAfter);

            await cmd.ExecuteNonQueryAsync();
        }
        finally { _dbLock.Release(); }
    }

    /// <summary>Records the start of an outage.</summary>
    public async Task<long> StartOutageAsync(DateTimeOffset startedAt, string errorType)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "INSERT INTO outages (started_at, error_type) VALUES (@start, @error); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@start", startedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@error", errorType);
            var result = await cmd.ExecuteScalarAsync();
            return (long)(result ?? 0);
        }
        finally { _dbLock.Release(); }
    }

    /// <summary>Closes an open outage by setting its end time and duration.</summary>
    public async Task CloseOutageAsync(DateTimeOffset endedAt)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                UPDATE outages
                SET ended_at = @end,
                    duration_seconds = (julianday(@end) - julianday(started_at)) * 86400.0
                WHERE ended_at IS NULL";
            cmd.Parameters.AddWithValue("@end", endedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _dbLock.Release(); }
    }

    /// <summary>Queries outages overlapping a time range.</summary>
    public async Task<IReadOnlyList<OutagePeriod>> QueryOutagesAsync(DateTimeOffset from, DateTimeOffset to)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            var results = new List<OutagePeriod>();
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT id, started_at, ended_at, duration_seconds, error_type
                FROM outages
                WHERE started_at <= @to AND (ended_at >= @from OR ended_at IS NULL)
                ORDER BY started_at ASC";
            cmd.Parameters.AddWithValue("@from", from.ToString("O"));
            cmd.Parameters.AddWithValue("@to", to.ToString("O"));
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new OutagePeriod
                {
                    Id = reader.GetInt64(0),
                    StartedAt = DateTimeOffset.Parse(reader.GetString(1)),
                    EndedAt = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                    DurationSeconds = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    ErrorType = reader.IsDBNull(4) ? null : reader.GetString(4),
                });
            }
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        _dbLock.Dispose();
    }

    private void EnsureConnected()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static UsagePoll ReadPollFromReader(SqliteDataReader reader)
    {
        return new UsagePoll(
            Id: reader.GetInt64(0),
            Timestamp: DateTimeOffset.Parse(reader.GetString(1)),
            FiveHourUtilization: reader.GetDouble(2),
            FiveHourResetsAt: reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            SevenDayUtilization: reader.IsDBNull(4) ? null : reader.GetDouble(4),
            SevenDayResetsAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)));
    }

    private static string GetDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CCStats", "ccstats.db");
    }
}
