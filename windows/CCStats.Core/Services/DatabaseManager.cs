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

    private const int CurrentSchemaVersion = 2;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync();

        // Create schema version tracking
        await ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL
            );
            """);

        var version = await GetSchemaVersionAsync();

        if (version == 0)
        {
            // Fresh install or legacy DB without versioning — create all tables
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

            await SetSchemaVersionAsync(1);
            version = 1;
        }

        // Migration v1 → v2: add account_id column to usage_polls for multi-account data separation
        if (version < 2)
        {
            await MigrateV1ToV2Async();
            version = 2;
        }

        // Future migrations go here: if (version < 3) { ... }
    }

    private async Task<int> GetSchemaVersionAsync()
    {
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT MAX(version) FROM schema_version;";
            var result = await cmd.ExecuteScalarAsync();
            return result is DBNull or null ? 0 : Convert.ToInt32(result);
        }
        catch
        {
            return 0; // table doesn't exist yet
        }
    }

    private async Task SetSchemaVersionAsync(int version)
    {
        await ExecuteNonQueryAsync("DELETE FROM schema_version;");
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT INTO schema_version (version) VALUES (@v);";
        cmd.Parameters.AddWithValue("@v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Migration v1 → v2: Adds account_id to usage_polls so data persists across re-auth.
    /// Existing data gets a default account_id if one exists.
    /// </summary>
    private async Task MigrateV1ToV2Async()
    {
        // Check if column already exists (idempotent)
        var hasColumn = false;
        try
        {
            await using var pragma = _connection!.CreateCommand();
            pragma.CommandText = "SELECT account_id FROM usage_polls LIMIT 0;";
            await pragma.ExecuteNonQueryAsync();
            hasColumn = true;
        }
        catch { /* column doesn't exist */ }

        if (!hasColumn)
        {
            await ExecuteNonQueryAsync("ALTER TABLE usage_polls ADD COLUMN account_id TEXT;");

            // Backfill: assign existing data to the first account file found
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CCStats");
            if (Directory.Exists(appDataDir))
            {
                var accountFile = Directory.GetFiles(appDataDir, "account_*.dat").FirstOrDefault();
                if (accountFile is not null)
                {
                    var accountId = Path.GetFileNameWithoutExtension(accountFile).Replace("account_", "");
                    await using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "UPDATE usage_polls SET account_id = @id WHERE account_id IS NULL;";
                    cmd.Parameters.AddWithValue("@id", accountId);
                    var updated = await cmd.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"[DB] Migration v1→v2: assigned {updated} polls to account {accountId}");
                }
            }
        }

        await SetSchemaVersionAsync(2);
        System.Diagnostics.Debug.WriteLine("[DB] Schema migrated to v2");
    }

    public async Task<long> InsertPollAsync(UsagePoll poll)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usage_polls (timestamp, five_hour_utilization, five_hour_resets_at, seven_day_utilization, seven_day_resets_at, account_id)
                VALUES (@timestamp, @fiveHour, @fiveHourResets, @sevenDay, @sevenDayResets, @accountId);
                SELECT last_insert_rowid();
                """;

            cmd.Parameters.AddWithValue("@timestamp", poll.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@fiveHour", poll.FiveHourUtilization);
            cmd.Parameters.AddWithValue("@fiveHourResets", poll.FiveHourResetsAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sevenDay", poll.SevenDayUtilization ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sevenDayResets", poll.SevenDayResetsAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@accountId", poll.AccountId ?? (object)DBNull.Value);

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
                SELECT id, timestamp, five_hour_utilization, five_hour_resets_at, seven_day_utilization, seven_day_resets_at, account_id
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

    public async Task<IReadOnlyList<ResetEvent>> QueryResetEventsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        EnsureConnected();
        await _dbLock.WaitAsync();
        try
        {
            await using var cmd = _connection!.CreateCommand();
            cmd.CommandText = """
                SELECT timestamp, window_type, utilization_before, utilization_after
                FROM reset_events
                WHERE timestamp >= @from AND timestamp <= @to
                ORDER BY timestamp ASC;
                """;
            cmd.Parameters.AddWithValue("@from", from.ToString("O"));
            cmd.Parameters.AddWithValue("@to", to.ToString("O"));

            var events = new List<ResetEvent>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new ResetEvent(
                    Timestamp: DateTimeOffset.Parse(reader.GetString(0)),
                    WindowType: reader.GetString(1),
                    UtilizationBefore: reader.GetDouble(2),
                    UtilizationAfter: reader.GetDouble(3)));
            }
            return events;
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
            SevenDayResetsAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            AccountId: reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null);
    }

    private static string GetDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CCStats", "ccstats.db");
    }
}
