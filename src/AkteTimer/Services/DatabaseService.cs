using System.IO;
using Microsoft.Data.Sqlite;
using AkteTimer.Models;

namespace AkteTimer.Services;

public sealed class DatabaseService
{
    private const string TimeEntrySelect = """
        SELECT te.id, te.matter_id, te.start_utc, te.end_utc, te.note, te.hashtag, te.created_utc, te.updated_utc,
               te.manual_adjustment, te.billed, te.billed_utc, te.billing_batch_id, m.file_ref
        FROM TimeEntries te
        JOIN Matters m ON te.matter_id = m.id
        """;

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
              custom_fee_factor REAL NULL,
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

            CREATE TABLE IF NOT EXISTS BillingBatches (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_utc TEXT NOT NULL,
              finalized_utc TEXT NULL,
              pdf_path TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS BillingCases (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              batch_id INTEGER NOT NULL,
              matter_id INTEGER NOT NULL,
              billing_type INTEGER NOT NULL,
              approved_utc TEXT NULL,
              tracked_minutes INTEGER NOT NULL DEFAULT 0,
              dummy_minutes INTEGER NOT NULL DEFAULT 0,
              total_minutes INTEGER NOT NULL DEFAULT 0,
              tracked_amount REAL NOT NULL DEFAULT 0,
              dummy_amount REAL NOT NULL DEFAULT 0,
              total_amount REAL NOT NULL DEFAULT 0,
              note_for_staff TEXT NULL,
              rvg_signature TEXT NULL,
              rvg_total REAL NOT NULL DEFAULT 0,
              rvg_is_difference INTEGER NOT NULL DEFAULT 0,
              rvg_base_signature TEXT NULL,
              rvg_base_total REAL NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS BillingAdjustments (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              case_id INTEGER NOT NULL,
              minutes_delta INTEGER NOT NULL,
              reason TEXT NULL,
              amount_delta REAL NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS RvgBillingSnapshots (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              matter_id INTEGER NOT NULL,
              billed_utc TEXT NOT NULL,
              signature TEXT NOT NULL,
              total REAL NOT NULL,
              batch_id INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entries_single_running
              ON TimeEntries (CASE WHEN end_utc IS NULL THEN 1 END);
            """;
        command.ExecuteNonQuery();
        EnsureHashtagColumn(connection);
        EnsureMatterColumns(connection);
        EnsureTimeEntryBillingColumns(connection);
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
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, custom_fee_factor,
                   target_rate_eur_per_hour, hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled,
                   settlement_fee_1_0_enabled, settlement_fee_1_5_enabled
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

    public Matter? GetMatterById(long matterId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, custom_fee_factor,
                   target_rate_eur_per_hour, hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled,
                   settlement_fee_1_0_enabled, settlement_fee_1_5_enabled
            FROM Matters
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", matterId);
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
            INSERT INTO Matters (file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, custom_fee_factor,
                                 target_rate_eur_per_hour, hourly_rate_eur_per_hour, business_fee_1_3_enabled,
                                 term_fee_1_2_enabled, settlement_fee_1_0_enabled, settlement_fee_1_5_enabled)
            VALUES ($file_ref, NULL, 0, $billing_type, $subject_value_eur, $fee_factor, $custom_fee_factor,
                    $target_rate_eur_per_hour, $hourly_rate_eur_per_hour, $business_fee_1_3_enabled,
                    $term_fee_1_2_enabled, $settlement_fee_1_0_enabled, $settlement_fee_1_5_enabled);
            """;
        insert.Parameters.AddWithValue("$file_ref", fileRef);
        insert.Parameters.AddWithValue("$billing_type", "hourly");
        insert.Parameters.AddWithValue("$subject_value_eur", 0d);
        insert.Parameters.AddWithValue("$fee_factor", DBNull.Value);
        insert.Parameters.AddWithValue("$custom_fee_factor", DBNull.Value);
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
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, custom_fee_factor,
                   target_rate_eur_per_hour, hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled,
                   settlement_fee_1_0_enabled, settlement_fee_1_5_enabled
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
        command.CommandText = $"""
            {TimeEntrySelect}
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
        select.CommandText = $"""
            {TimeEntrySelect}
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
        command.CommandText = $"""
            {TimeEntrySelect}
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
            SELECT m.id, m.file_ref, m.title, m.is_archived, m.billing_type, m.subject_value_eur, m.fee_factor,
                   m.custom_fee_factor, m.target_rate_eur_per_hour, m.hourly_rate_eur_per_hour,
                   m.business_fee_1_3_enabled, m.term_fee_1_2_enabled, m.settlement_fee_1_0_enabled,
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
            SELECT id, file_ref, title, is_archived, billing_type, subject_value_eur, fee_factor, custom_fee_factor,
                   target_rate_eur_per_hour, hourly_rate_eur_per_hour, business_fee_1_3_enabled, term_fee_1_2_enabled,
                   settlement_fee_1_0_enabled, settlement_fee_1_5_enabled
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
            {TimeEntrySelect}
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
        command.CommandText = $"""
            {TimeEntrySelect}
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

    public List<TimeEntry> GetUnbilledEntries(bool onlyCompleted = true)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        var completionFilter = onlyCompleted ? " AND te.end_utc IS NOT NULL" : string.Empty;
        command.CommandText = $"""
            {TimeEntrySelect}
            WHERE te.billed = 0{completionFilter}
            ORDER BY te.start_utc ASC;
            """;

        using var reader = command.ExecuteReader();
        var entries = new List<TimeEntry>();
        while (reader.Read())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public List<TimeEntry> GetTimeEntriesByIds(IReadOnlyCollection<long> entryIds, bool onlyCompleted = true)
    {
        if (entryIds.Count == 0)
        {
            return new List<TimeEntry>();
        }

        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        var entryFilters = string.Join(", ", entryIds.Select((_, index) => $"$entry_{index}"));
        var completionFilter = onlyCompleted ? " AND te.end_utc IS NOT NULL" : string.Empty;
        command.CommandText = $"""
            {TimeEntrySelect}
            WHERE te.id IN ({entryFilters}){completionFilter}
            ORDER BY te.start_utc ASC;
            """;
        var index = 0;
        foreach (var entryId in entryIds)
        {
            command.Parameters.AddWithValue($"$entry_{index}", entryId);
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

    public void UpdateTimeEntryNote(long entryId, string? note)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE TimeEntries
                SET note = $note, updated_utc = $updated_utc
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note);
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
        command.CommandText = $"""
            {TimeEntrySelect}
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
        select.CommandText = $"""
            {TimeEntrySelect}
            WHERE te.id = $id;
            """;
        select.Parameters.AddWithValue("$id", entryId);
        using var reader = select.ExecuteReader();
        reader.Read();
        var entry = MapTimeEntry(reader);
        transaction.Commit();
        return entry;
    }

    public void DeleteTimeEntry(long entryId)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TimeEntries WHERE id = $id;";
            command.Parameters.AddWithValue("$id", entryId);
            command.ExecuteNonQuery();
        });
    }

    public (TimeEntry UpdatedEntry, TimeEntry NewEntry) SplitTimeEntry(long entryId, DateTime splitUtc)
    {
        var now = DateTime.UtcNow;
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"""
            {TimeEntrySelect}
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
        selectUpdated.CommandText = $"""
            {TimeEntrySelect}
            WHERE te.id = $id;
            """;
        selectUpdated.Parameters.AddWithValue("$id", entryId);
        using var updatedReader = selectUpdated.ExecuteReader();
        updatedReader.Read();
        var updatedEntry = MapTimeEntry(updatedReader);

        using var selectNew = connection.CreateCommand();
        selectNew.Transaction = transaction;
        selectNew.CommandText = $"""
            {TimeEntrySelect}
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
                    custom_fee_factor = $custom_fee_factor,
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
            command.Parameters.AddWithValue("$fee_factor", matter.FeeFactor.HasValue ? (object)(double)matter.FeeFactor.Value : DBNull.Value);
            command.Parameters.AddWithValue("$custom_fee_factor", matter.CustomFeeFactor.HasValue ? (object)(double)matter.CustomFeeFactor.Value : DBNull.Value);
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

    public BillingBatch CreateBillingBatch(DateTime createdUtc)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO BillingBatches (created_utc, finalized_utc, pdf_path)
            VALUES ($created_utc, NULL, NULL);
            """;
        insert.Parameters.AddWithValue("$created_utc", createdUtc.ToString("o"));
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id, created_utc, finalized_utc, pdf_path
            FROM BillingBatches
            WHERE rowid = last_insert_rowid();
            """;
        using var reader = select.ExecuteReader();
        reader.Read();
        var batch = MapBillingBatch(reader);
        transaction.Commit();
        return batch;
    }

    public BillingBatch? GetBillingBatchById(long batchId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_utc, finalized_utc, pdf_path
            FROM BillingBatches
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", batchId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapBillingBatch(reader);
    }

    public BillingCase CreateBillingCase(BillingCase billingCase)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO BillingCases (
                batch_id, matter_id, billing_type, approved_utc, tracked_minutes, dummy_minutes, total_minutes,
                tracked_amount, dummy_amount, total_amount, note_for_staff, rvg_signature, rvg_total, rvg_is_difference,
                rvg_base_signature, rvg_base_total)
            VALUES (
                $batch_id, $matter_id, $billing_type, $approved_utc, $tracked_minutes, $dummy_minutes, $total_minutes,
                $tracked_amount, $dummy_amount, $total_amount, $note_for_staff, $rvg_signature, $rvg_total,
                $rvg_is_difference, $rvg_base_signature, $rvg_base_total);
            """;
        insert.Parameters.AddWithValue("$batch_id", billingCase.BatchId);
        insert.Parameters.AddWithValue("$matter_id", billingCase.MatterId);
        insert.Parameters.AddWithValue("$billing_type", (int)billingCase.BillingType);
        insert.Parameters.AddWithValue("$approved_utc", billingCase.ApprovedUtc.HasValue ? billingCase.ApprovedUtc.Value.ToString("o") : DBNull.Value);
        insert.Parameters.AddWithValue("$tracked_minutes", billingCase.TrackedMinutes);
        insert.Parameters.AddWithValue("$dummy_minutes", billingCase.DummyMinutes);
        insert.Parameters.AddWithValue("$total_minutes", billingCase.TotalMinutes);
        insert.Parameters.AddWithValue("$tracked_amount", (double)billingCase.TrackedAmount);
        insert.Parameters.AddWithValue("$dummy_amount", (double)billingCase.DummyAmount);
        insert.Parameters.AddWithValue("$total_amount", (double)billingCase.TotalAmount);
        insert.Parameters.AddWithValue("$note_for_staff", string.IsNullOrWhiteSpace(billingCase.NoteForStaff) ? DBNull.Value : billingCase.NoteForStaff);
        insert.Parameters.AddWithValue("$rvg_signature", string.IsNullOrWhiteSpace(billingCase.RvgSignature) ? DBNull.Value : billingCase.RvgSignature);
        insert.Parameters.AddWithValue("$rvg_total", (double)billingCase.RvgTotal);
        insert.Parameters.AddWithValue("$rvg_is_difference", billingCase.RvgIsDifference ? 1 : 0);
        insert.Parameters.AddWithValue("$rvg_base_signature", string.IsNullOrWhiteSpace(billingCase.RvgBaseSignature) ? DBNull.Value : billingCase.RvgBaseSignature);
        insert.Parameters.AddWithValue("$rvg_base_total", (double)billingCase.RvgBaseTotal);
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id, batch_id, matter_id, billing_type, approved_utc, tracked_minutes, dummy_minutes, total_minutes,
                   tracked_amount, dummy_amount, total_amount, note_for_staff, rvg_signature, rvg_total,
                   rvg_is_difference, rvg_base_signature, rvg_base_total
            FROM BillingCases
            WHERE rowid = last_insert_rowid();
            """;
        using var reader = select.ExecuteReader();
        reader.Read();
        var createdCase = MapBillingCase(reader);
        transaction.Commit();
        return createdCase;
    }

    public BillingCase? GetBillingCaseById(long caseId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, batch_id, matter_id, billing_type, approved_utc, tracked_minutes, dummy_minutes, total_minutes,
                   tracked_amount, dummy_amount, total_amount, note_for_staff, rvg_signature, rvg_total,
                   rvg_is_difference, rvg_base_signature, rvg_base_total
            FROM BillingCases
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", caseId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapBillingCase(reader);
    }

    public void UpdateBillingCaseApprovedUtc(long caseId, DateTime? approvedUtc)
    {
        ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE BillingCases
                SET approved_utc = $approved_utc
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$approved_utc", approvedUtc.HasValue ? approvedUtc.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("$id", caseId);
            command.ExecuteNonQuery();
        });
    }

    public List<BillingCase> GetBillingCasesForBatch(long batchId)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, batch_id, matter_id, billing_type, approved_utc, tracked_minutes, dummy_minutes, total_minutes,
                   tracked_amount, dummy_amount, total_amount, note_for_staff, rvg_signature, rvg_total,
                   rvg_is_difference, rvg_base_signature, rvg_base_total
            FROM BillingCases
            WHERE batch_id = $batch_id
            ORDER BY id ASC;
            """;
        command.Parameters.AddWithValue("$batch_id", batchId);
        using var reader = command.ExecuteReader();
        var cases = new List<BillingCase>();
        while (reader.Read())
        {
            cases.Add(MapBillingCase(reader));
        }

        return cases;
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
            Billed = !reader.IsDBNull(9) && reader.GetInt64(9) != 0,
            BilledUtc = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)).ToUniversalTime(),
            BillingBatchId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            MatterFileRef = reader.IsDBNull(12) ? null : reader.GetString(12)
        };
    }

    private static Matter MapMatter(SqliteDataReader reader)
    {
        var billingTypeValue = reader.IsDBNull(4) ? "hourly" : reader.GetString(4);
        var billingType = string.Equals(billingTypeValue, "rvg", StringComparison.OrdinalIgnoreCase)
            ? BillingType.Rvg
            : BillingType.Hourly;
        var subjectValue = GetDecimalOrDefault(reader, 5);
        var feeFactor = GetNullableDecimal(reader, 6);
        var customFeeFactor = GetNullableDecimal(reader, 7);
        var targetRate = GetDecimalOrDefault(reader, 8);
        var hourlyRate = GetDecimalOrDefault(reader, 9, 230m);
        var businessFee13Enabled = !reader.IsDBNull(10) && reader.GetInt64(10) == 1;
        var termFee12Enabled = !reader.IsDBNull(11) && reader.GetInt64(11) == 1;
        var settlementFee10Enabled = !reader.IsDBNull(12) && reader.GetInt64(12) == 1;
        var settlementFee15Enabled = !reader.IsDBNull(13) && reader.GetInt64(13) == 1;

        return new Matter
        {
            Id = reader.GetInt64(0),
            FileRef = reader.GetString(1),
            Title = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsArchived = reader.GetInt64(3) == 1,
            BillingType = billingType,
            SubjectValueEur = subjectValue,
            FeeFactor = feeFactor,
            CustomFeeFactor = customFeeFactor,
            TargetRateEurPerHour = targetRate,
            HourlyRateEurPerHour = hourlyRate,
            BusinessFee13Enabled = businessFee13Enabled,
            TermFee12Enabled = termFee12Enabled,
            SettlementFee10Enabled = settlementFee10Enabled,
            SettlementFee15Enabled = settlementFee15Enabled
        };
    }

    private static BillingBatch MapBillingBatch(SqliteDataReader reader)
    {
        return new BillingBatch
        {
            Id = reader.GetInt64(0),
            CreatedUtc = DateTime.Parse(reader.GetString(1)).ToUniversalTime(),
            FinalizedUtc = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
            PdfPath = reader.IsDBNull(3) ? null : reader.GetString(3)
        };
    }

    private static BillingCase MapBillingCase(SqliteDataReader reader)
    {
        var billingTypeValue = reader.GetInt32(3);
        var billingType = Enum.IsDefined(typeof(BillingType), billingTypeValue)
            ? (BillingType)billingTypeValue
            : BillingType.Hourly;

        return new BillingCase
        {
            Id = reader.GetInt64(0),
            BatchId = reader.GetInt64(1),
            MatterId = reader.GetInt64(2),
            BillingType = billingType,
            ApprovedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
            TrackedMinutes = reader.GetInt32(5),
            DummyMinutes = reader.GetInt32(6),
            TotalMinutes = reader.GetInt32(7),
            TrackedAmount = (decimal)reader.GetDouble(8),
            DummyAmount = (decimal)reader.GetDouble(9),
            TotalAmount = (decimal)reader.GetDouble(10),
            NoteForStaff = reader.IsDBNull(11) ? null : reader.GetString(11),
            RvgSignature = reader.IsDBNull(12) ? null : reader.GetString(12),
            RvgTotal = (decimal)reader.GetDouble(13),
            RvgIsDifference = !reader.IsDBNull(14) && reader.GetInt64(14) == 1,
            RvgBaseSignature = reader.IsDBNull(15) ? null : reader.GetString(15),
            RvgBaseTotal = (decimal)reader.GetDouble(16)
        };
    }

    private static decimal GetDecimalOrDefault(SqliteDataReader reader, int index, decimal defaultValue = 0m)
    {
        return reader.IsDBNull(index) ? defaultValue : (decimal)reader.GetDouble(index);
    }

    private static decimal? GetNullableDecimal(SqliteDataReader reader, int index)
    {
        return reader.IsDBNull(index) ? null : (decimal)reader.GetDouble(index);
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

        if (!columns.Contains("custom_fee_factor"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN custom_fee_factor REAL NULL;");
        }

        if (!columns.Contains("target_rate_eur_per_hour"))
        {
            additions.Add("ALTER TABLE Matters ADD COLUMN target_rate_eur_per_hour REAL NULL;");
        }
        var hasHourlyRateColumn = columns.Contains("hourly_rate_eur_per_hour");
        if (!hasHourlyRateColumn)
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

        if (hasHourlyRateColumn)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Matters SET hourly_rate_eur_per_hour = 230.0 WHERE hourly_rate_eur_per_hour IS NULL;";
            update.ExecuteNonQuery();
        }
    }

    private static void EnsureTimeEntryBillingColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(TimeEntries);";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        var additions = new List<string>();
        if (!columns.Contains("billed"))
        {
            additions.Add("ALTER TABLE TimeEntries ADD COLUMN billed INTEGER NOT NULL DEFAULT 0;");
        }

        if (!columns.Contains("billed_utc"))
        {
            additions.Add("ALTER TABLE TimeEntries ADD COLUMN billed_utc TEXT NULL;");
        }

        if (!columns.Contains("billing_batch_id"))
        {
            additions.Add("ALTER TABLE TimeEntries ADD COLUMN billing_batch_id INTEGER NULL;");
        }

        foreach (var statement in additions)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = statement;
            alter.ExecuteNonQuery();
        }
    }
}
