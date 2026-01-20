using System.IO;
using Microsoft.Data.Sqlite;
using AkteTimer.Models;

namespace AkteTimer.Services;

public sealed class DatabaseService
{
    private readonly string? _databasePath;
    private readonly DataDirectoryService? _dataDirectoryService;

    public DatabaseService(DataDirectoryService dataDirectoryService)
    {
        _dataDirectoryService = dataDirectoryService;
    }

    public DatabaseService(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    public string DatabasePath => ResolveDatabasePath();

    public void Initialize()
    {
        var databasePath = ResolveDatabasePath();
        var folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Matters (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              file_ref TEXT NOT NULL UNIQUE,
              title TEXT NULL,
              is_archived INTEGER NOT NULL DEFAULT 0,
              billing_type TEXT NOT NULL DEFAULT 'hourly',
              subject_value_eur REAL NULL,
              fee_factor REAL NULL,
              target_rate_eur_per_hour REAL NULL,
              hourly_rate_eur_per_hour REAL NOT NULL DEFAULT 230.0,
              business_fee_1_3_enabled INTEGER NOT NULL DEFAULT 0,
              term_fee_1_2_enabled INTEGER NOT NULL DEFAULT 0,
              settlement_fee_1_0_enabled INTEGER NOT NULL DEFAULT 0,
              settlement_fee_1_5_enabled INTEGER NOT NULL DEFAULT 0
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
        EnsureMatterColumns(connection);
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={ResolveDatabasePath()}");
    }

    private string ResolveDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(_databasePath))
        {
            return _databasePath;
        }

        if (_dataDirectoryService == null)
        {
            throw new InvalidOperationException("Database path is not configured.");
        }

        return _dataDirectoryService.DatabasePath;
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
        command.CommandText = """
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, target_rate_eur_per_hour,
                   hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled, settlement_fee_1_0_enabled,
                   settlement_fee_1_5_enabled
            FROM Matters
            WHERE file_ref = $file_ref;
            """;
        command.Parameters.AddWithValue("$file_ref", fileRef);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapMatter(reader);
    }

    public Matter CreateMatter(string fileRef)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Matters (file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, target_rate_eur_per_hour,
                                 hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled, settlement_fee_1_0_enabled,
                                 settlement_fee_1_5_enabled)
            VALUES ($file_ref, NULL, 0, $billing_type, $subject_value_eur, $fee_factor, $target_rate_eur_per_hour,
                    $hourly_rate_eur_per_hour, $business_fee_1_3_enabled, $term_fee_1_2_enabled, $settlement_fee_1_0_enabled,
                    $settlement_fee_1_5_enabled);
            """;
        insert.Parameters.AddWithValue("$file_ref", fileRef);
        insert.Parameters.AddWithValue("$billing_type", "hourly");
        insert.Parameters.AddWithValue("$subject_value_eur", 0d);
        insert.Parameters.AddWithValue("$fee_factor", 1d);
        insert.Parameters.AddWithValue("$target_rate_eur_per_hour", 0d);
        insert.Parameters.AddWithValue("$hourly_rate_eur_per_hour", 230d);
        insert.Parameters.AddWithValue("$business_fee_1_3_enabled", 0);
        insert.Parameters.AddWithValue("$term_fee_1_2_enabled", 0);
        insert.Parameters.AddWithValue("$settlement_fee_1_0_enabled", 0);
        insert.Parameters.AddWithValue("$settlement_fee_1_5_enabled", 0);
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, target_rate_eur_per_hour,
                   hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled, settlement_fee_1_0_enabled,
                   settlement_fee_1_5_enabled
            FROM Matters
            WHERE file_ref = $file_ref;
            """;
        select.Parameters.AddWithValue("$file_ref", fileRef);
        using var reader = select.ExecuteReader();
        reader.Read();
        var matter = MapMatter(reader);
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
            SELECT m.id, m.file_ref, m.title, m.is_archived, m.billing_type, m.subject_value_eur, m.fee_factor, m.target_rate_eur_per_hour,
                   m.hourly_rate_eur_per_hour, m.business_fee_1_3_enabled, m.term_fee_1_2_enabled, m.settlement_fee_1_0_enabled,
                   m.settlement_fee_1_5_enabled
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
            matters.Add(MapMatter(reader));
        }

        return matters;
    }

    public List<Matter> GetAllMatters()
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, target_rate_eur_per_hour,
                   hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled, settlement_fee_1_0_enabled,
                   settlement_fee_1_5_enabled
            FROM Matters
            ORDER BY file_ref ASC;
            """;
        using var reader = command.ExecuteReader();
        var matters = new List<Matter>();
        while (reader.Read())
        {
            matters.Add(MapMatter(reader));
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

    public List<TimeEntry> GetEntriesForMatter(long matterId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.matter_id = $matter_id
            ORDER BY te.start_utc ASC;
            """;
        command.Parameters.AddWithValue("$matter_id", matterId);

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

    public TimeEntry? GetTimeEntryById(long entryId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.id = $id;
            """;
        command.Parameters.AddWithValue("$id", entryId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapTimeEntry(reader);
    }

    public TimeEntry UpdateTimeEntry(long entryId, long matterId, DateTime startUtc, DateTime endUtc, string? hashtag, string? note)
    {
        var now = DateTime.UtcNow;
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE TimeEntries
            SET matter_id = $matter_id,
                start_utc = $start_utc,
                end_utc = $end_utc,
                note = $note,
                hashtag = $hashtag,
                updated_utc = $updated_utc,
                manual_adjustment = 1
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$matter_id", matterId);
        command.Parameters.AddWithValue("$start_utc", startUtc.ToString("o"));
        command.Parameters.AddWithValue("$end_utc", endUtc.ToString("o"));
        command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note);
        command.Parameters.AddWithValue("$hashtag", string.IsNullOrWhiteSpace(hashtag) ? DBNull.Value : hashtag);
        command.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
        command.Parameters.AddWithValue("$id", entryId);
        command.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.id = $id;
            """;
        select.Parameters.AddWithValue("$id", entryId);
        using var reader = select.ExecuteReader();
        reader.Read();
        var entry = MapTimeEntry(reader);
        transaction.Commit();
        return entry;
    }

    public (TimeEntry UpdatedEntry, TimeEntry NewEntry) SplitTimeEntry(long entryId, DateTime splitUtc)
    {
        var now = DateTime.UtcNow;
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.id = $id;
            """;
        select.Parameters.AddWithValue("$id", entryId);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Eintrag nicht gefunden.");
        }

        var entry = MapTimeEntry(reader);
        if (entry.EndUtc == null)
        {
            throw new InvalidOperationException("Laufende Einträge können nicht gesplittet werden.");
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE TimeEntries
            SET end_utc = $end_utc,
                updated_utc = $updated_utc,
                manual_adjustment = 1
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$end_utc", splitUtc.ToString("o"));
        update.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
        update.Parameters.AddWithValue("$id", entryId);
        update.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO TimeEntries (matter_id, start_utc, end_utc, note, hashtag, created_utc, updated_utc, manual_adjustment)
            VALUES ($matter_id, $start_utc, $end_utc, $note, $hashtag, $created_utc, $updated_utc, 1);
            """;
        insert.Parameters.AddWithValue("$matter_id", entry.MatterId);
        insert.Parameters.AddWithValue("$start_utc", splitUtc.ToString("o"));
        insert.Parameters.AddWithValue("$end_utc", entry.EndUtc.Value.ToString("o"));
        insert.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(entry.Note) ? DBNull.Value : entry.Note);
        insert.Parameters.AddWithValue("$hashtag", string.IsNullOrWhiteSpace(entry.Hashtag) ? DBNull.Value : entry.Hashtag);
        insert.Parameters.AddWithValue("$created_utc", now.ToString("o"));
        insert.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
        insert.ExecuteNonQuery();

        using var selectUpdated = connection.CreateCommand();
        selectUpdated.Transaction = transaction;
        selectUpdated.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.id = $id;
            """;
        selectUpdated.Parameters.AddWithValue("$id", entryId);
        using var updatedReader = selectUpdated.ExecuteReader();
        updatedReader.Read();
        var updatedEntry = MapTimeEntry(updatedReader);

        using var selectNew = connection.CreateCommand();
        selectNew.Transaction = transaction;
        selectNew.CommandText = """
            SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc, te.manual_adjustment, m.file_ref
            FROM TimeEntries te
            JOIN Matters m ON te.matter_id = m.id
            WHERE te.rowid = last_insert_rowid();
            """;
        using var newReader = selectNew.ExecuteReader();
        newReader.Read();
        var newEntry = MapTimeEntry(newReader);

        transaction.Commit();
        return (updatedEntry, newEntry);
    }

    public void UpdateMatter(Matter matter)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Matters
                SET billing_type = $billing_type,
                    subject_value_eur = $subject_value_eur,
                    fee_factor = $fee_factor,
                    target_rate_eur_per_hour = $target_rate_eur_per_hour,
                    hourly_rate_eur_per_hour = $hourly_rate_eur_per_hour,
                    business_fee_1_3_enabled = $business_fee_1_3_enabled,
                    term_fee_1_2_enabled = $term_fee_1_2_enabled,
                    settlement_fee_1_0_enabled = $settlement_fee_1_0_enabled,
                    settlement_fee_1_5_enabled = $settlement_fee_1_5_enabled
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$billing_type", matter.BillingType == BillingType.Rvg ? "rvg" : "hourly");
            command.Parameters.AddWithValue("$subject_value_eur", (double)matter.SubjectValueEur);
            command.Parameters.AddWithValue("$fee_factor", (double)matter.FeeFactor);
            command.Parameters.AddWithValue("$target_rate_eur_per_hour", (double)matter.TargetRateEurPerHour);
            command.Parameters.AddWithValue("$hourly_rate_eur_per_hour", (double)matter.HourlyRateEurPerHour);
            command.Parameters.AddWithValue("$business_fee_1_3_enabled", matter.BusinessFee13Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$term_fee_1_2_enabled", matter.TermFee12Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$settlement_fee_1_0_enabled", matter.SettlementFee10Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$settlement_fee_1_5_enabled", matter.SettlementFee15Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$id", matter.Id);
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

    private static Matter MapMatter(SqliteDataReader reader)
    {
        var billingTypeValue = reader.IsDBNull(4) ? "hourly" : reader.GetString(4);
        var billingType = string.Equals(billingTypeValue, "rvg", StringComparison.OrdinalIgnoreCase)
            ? BillingType.Rvg
            : BillingType.Hourly;
        var subjectValue = GetDecimalOrDefault(reader, 5);
        var feeFactor = GetDecimalOrDefault(reader, 6, 1.0m);
        var targetRate = GetDecimalOrDefault(reader, 7);
        var hourlyRate = GetDecimalOrDefault(reader, 8, 230m);
        var businessFee13Enabled = !reader.IsDBNull(9) && reader.GetInt64(9) == 1;
        var termFee12Enabled = !reader.IsDBNull(10) && reader.GetInt64(10) == 1;
        var settlementFee10Enabled = !reader.IsDBNull(11) && reader.GetInt64(11) == 1;
        var settlementFee15Enabled = !reader.IsDBNull(12) && reader.GetInt64(12) == 1;

        return new Matter
        {
            Id = reader.GetInt64(0),
            FileRef = reader.GetString(1),
            Title = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsArchived = reader.GetInt64(3) == 1,
            BillingType = billingType,
            SubjectValueEur = subjectValue,
            FeeFactor = feeFactor,
            TargetRateEurPerHour = targetRate,
            HourlyRateEurPerHour = hourlyRate,
            BusinessFee13Enabled = businessFee13Enabled,
            TermFee12Enabled = termFee12Enabled,
            SettlementFee10Enabled = settlementFee10Enabled,
            SettlementFee15Enabled = settlementFee15Enabled
        };
    }

    private static decimal GetDecimalOrDefault(SqliteDataReader reader, int index, decimal defaultValue = 0m)
    {
        return reader.IsDBNull(index) ? defaultValue : (decimal)reader.GetDouble(index);
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

    private static void EnsureMatterColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Matters);";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        var additions = new List<string>();
        if (!columns.Contains("billing_type"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN billing_type TEXT NOT NULL DEFAULT 'hourly';");
        }

        if (!columns.Contains("subject_value_eur"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN subject_value_eur REAL NULL;");
        }

        if (!columns.Contains("fee_factor"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN fee_factor REAL NULL;");
        }

        if (!columns.Contains("target_rate_eur_per_hour"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN target_rate_eur_per_hour REAL NULL;");
        }
        if (!columns.Contains("hourly_rate_eur_per_hour"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN hourly_rate_eur_per_hour REAL NOT NULL DEFAULT 230.0;");
        }
        if (!columns.Contains("business_fee_1_3_enabled"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN business_fee_1_3_enabled INTEGER NOT NULL DEFAULT 0;");
        }
        if (!columns.Contains("term_fee_1_2_enabled"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN term_fee_1_2_enabled INTEGER NOT NULL DEFAULT 0;");
        }
        if (!columns.Contains("settlement_fee_1_0_enabled"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN settlement_fee_1_0_enabled INTEGER NOT NULL DEFAULT 0;");
        }
        if (!columns.Contains("settlement_fee_1_5_enabled"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN settlement_fee_1_5_enabled INTEGER NOT NULL DEFAULT 0;");
        }

        foreach (var statement in additions)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = statement;
            alter.ExecuteNonQuery();
        }
    }
}
