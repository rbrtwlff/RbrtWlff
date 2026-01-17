using System.IO;
using Microsoft.Data.Sqlite;
using AkteTimer.Models;

namespace AkteTimer.Services;

public sealed class DatabaseService
{
    private readonly string _databasePath;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "AkteTimer");
        Directory.CreateDirectory(folder);
        _databasePath = Path.Combine(folder, "aktetimer.db");
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Matters (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              file_ref TEXT NOT NULL UNIQUE,
              title TEXT NULL,
              is_archived INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS TimeEntries (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              matter_id INTEGER NOT NULL,
              start_utc TEXT NOT NULL,
              end_utc TEXT NULL,
              note TEXT NULL,
              hashtag TEXT NULL,
              created_utc TEXT NOT NULL,
              updated_utc TEXT NOT NULL,
              manual_adjustment INTEGER NOT NULL DEFAULT 0,
              FOREIGN KEY(matter_id) REFERENCES Matters(id)
            );

            CREATE TABLE IF NOT EXISTS Settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entries_single_running
              ON TimeEntries (CASE WHEN end_utc IS NULL THEN 1 END);
            """;
        command.ExecuteNonQuery();
        EnsureHashtagColumn(connection);
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }

    public void ExecuteInTransaction(Action<SqliteConnection, SqliteTransaction> action)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        action(connection, transaction);
        transaction.Commit();
    }

    public Matter? GetMatterByFileRef(string fileRef)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_ref, title, is_archived FROM Matters WHERE file_ref = $file_ref;";
        command.Parameters.AddWithValue("$file_ref", fileRef);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new Matter
        {
            Id = reader.GetInt64(0),
            FileRef = reader.GetString(1),
            Title = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsArchived = reader.GetInt64(3) == 1
        };
    }

    public Matter CreateMatter(string fileRef)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO Matters (file_ref, title, is_archived) VALUES ($file_ref, NULL, 0);";
        insert.Parameters.AddWithValue("$file_ref", fileRef);
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id, file_ref, title, is_archived FROM Matters WHERE file_ref = $file_ref;";
        select.Parameters.AddWithValue("$file_ref", fileRef);
        using var reader = select.ExecuteReader();
        reader.Read();
        var matter = new Matter
        {
            Id = reader.GetInt64(0),
            FileRef = reader.GetString(1),
            Title = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsArchived = reader.GetInt64(3) == 1
        };
        transaction.Commit();
        return matter;
    }

    public TimeEntry? GetRunningEntry()
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.end_utc IS NULL
            ORDER BY te.start_utc DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapTimeEntry(reader);
    }

    public void StopRunningEntries(DateTime endUtc)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE TimeEntries SET end_utc = $end_utc, updated_utc = $updated_utc WHERE end_utc IS NULL;";
            command.Parameters.AddWithValue("$end_utc", endUtc.ToString("o"));
            command.Parameters.AddWithValue("$updated_utc", DateTime.UtcNow.ToString("o"));
            command.ExecuteNonQuery();
        });
    }

    public TimeEntry CreateTimeEntry(long matterId, DateTime startUtc, string hashtag)
    {
        var now = DateTime.UtcNow;
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO TimeEntries (matter_id, start_utc, end_utc, note, hashtag, created_utc, updated_utc, manual_adjustment)
            VALUES ($matter_id, $start_utc, NULL, NULL, $hashtag, $created_utc, $updated_utc, 0);
            """;
        insert.Parameters.AddWithValue("$matter_id", matterId);
        insert.Parameters.AddWithValue("$start_utc", startUtc.ToString("o"));
        insert.Parameters.AddWithValue("$hashtag", hashtag);
        insert.Parameters.AddWithValue("$created_utc", now.ToString("o"));
        insert.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.rowid = last_insert_rowid();
            """;
        using var reader = select.ExecuteReader();
        reader.Read();
        var entry = MapTimeEntry(reader);
        transaction.Commit();
        return entry;
    }

    public List<TimeEntry> GetTodayEntries()
    {
        var todayLocal = DateTime.Now.Date;
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1));

        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.start_utc >= $start AND te.start_utc < $end
            ORDER BY te.start_utc ASC;
            """;
        command.Parameters.AddWithValue("$start", startUtc.ToString("o"));
        command.Parameters.AddWithValue("$end", endUtc.ToString("o"));

        using var reader = command.ExecuteReader();
        var entries = new List<TimeEntry>();
        while (reader.Read())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public List<Matter> GetRecentMatters(int limit)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.id, m.file_ref, m.title, m.is_archived
            FROM Matters m
            JOIN TimeEntries te ON m.id = te.matter_id
            GROUP BY m.id
            ORDER BY MAX(te.start_utc) DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var matters = new List<Matter>();
        while (reader.Read())
        {
            matters.Add(new Matter
            {
                Id = reader.GetInt64(0),
                FileRef = reader.GetString(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsArchived = reader.GetInt64(3) == 1
            });
        }

        return matters;
    }

    public List<Matter> GetAllMatters()
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_ref, title, is_archived FROM Matters ORDER BY file_ref ASC;";
        using var reader = command.ExecuteReader();
        var matters = new List<Matter>();
        while (reader.Read())
        {
            matters.Add(new Matter
            {
                Id = reader.GetInt64(0),
                FileRef = reader.GetString(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsArchived = reader.GetInt64(3) == 1
            });
        }

        return matters;
    }

    public List<TimeEntry> GetEntriesInRange(DateTime startUtc, DateTime endUtc, IReadOnlyCollection<long> matterIds)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        var matterFilters = matterIds.Count == 0
            ? string.Empty
            : $" AND te.matter_id IN ({string.Join(", ", matterIds.Select((_, index) => $"$matter_{index}"))})";
        command.CommandText = $"""
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.start_utc >= $start AND te.start_utc < $end{matterFilters}
            ORDER BY te.start_utc ASC;
            """;
        command.Parameters.AddWithValue("$start", startUtc.ToString("o"));
        command.Parameters.AddWithValue("$end", endUtc.ToString("o"));
        var index = 0;
        foreach (var matterId in matterIds)
        {
            command.Parameters.AddWithValue($"$matter_{index}", matterId);
            index++;
        }

        using var reader = command.ExecuteReader();
        var entries = new List<TimeEntry>();
        while (reader.Read())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public string? GetSetting(string key)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM Settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = $value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        });
    }

    public void UpdateTimeEntryHashtag(long entryId, string hashtag)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE TimeEntries
                SET hashtag = $hashtag, updated_utc = $updated_utc
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$hashtag", hashtag);
            command.Parameters.AddWithValue("$updated_utc", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$id", entryId);
            command.ExecuteNonQuery();
        });
    }

    private static TimeEntry MapTimeEntry(SqliteDataReader reader)
    {
        return new TimeEntry
        {
            Id = reader.GetInt64(0),
            MatterId = reader.GetInt64(1),
            StartUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
            EndUtc = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
            Note = reader.IsDBNull(4) ? null : reader.GetString(4),
            Hashtag = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedUtc = DateTime.Parse(reader.GetString(6)).ToUniversalTime(),
            UpdatedUtc = DateTime.Parse(reader.GetString(7)).ToUniversalTime(),
            ManualAdjustment = reader.GetInt64(8) == 1,
            MatterFileRef = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }

    private static void EnsureHashtagColumn(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(TimeEntries);";
        using var reader = command.ExecuteReader();
        var hasColumn = false;
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, "hashtag", StringComparison.OrdinalIgnoreCase))
            {
                hasColumn = true;
                break;
            }
        }

        if (hasColumn)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE TimeEntries ADD COLUMN hashtag TEXT NULL;";
        alter.ExecuteNonQuery();
    }
}
