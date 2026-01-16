using System.Text.RegularExpressions;
using AkteTimer.Models;

namespace AkteTimer.Services;

public sealed class TimeEntryService
{
    private static readonly Regex FileRefRegex = new("^\\d{1,6}\\/\\d{2}$", RegexOptions.Compiled);
    private readonly DatabaseService _database;
    private readonly SettingsService _settings;

    public TimeEntryService(DatabaseService database, SettingsService settings)
    {
        _database = database;
        _settings = settings;
        ActiveMatterFileRef = _settings.LastMatter;
    }

    public event EventHandler? StateChanged;

    public string? ActiveMatterFileRef { get; private set; }

    public bool IsRunning => GetRunningEntry() != null;

    public bool IsValidFileRef(string fileRef) => FileRefRegex.IsMatch(fileRef);

    public TimeEntry? GetRunningEntry() => _database.GetRunningEntry();

    public Matter? GetMatterByFileRef(string fileRef) => _database.GetMatterByFileRef(fileRef);

    public Matter CreateMatter(string fileRef) => _database.CreateMatter(fileRef);

    public void ResumeRunningEntry(TimeEntry entry)
    {
        ActiveMatterFileRef = entry.MatterFileRef;
        if (!string.IsNullOrWhiteSpace(entry.MatterFileRef))
        {
            _settings.SetLastMatter(entry.MatterFileRef);
        }
        OnStateChanged();
    }

    public void StopRunningEntry(TimeEntry entry, DateTime endUtc)
    {
        _database.StopRunningEntry(entry.Id, endUtc);
        OnStateChanged();
    }

    public bool ToggleStartPause()
    {
        var running = GetRunningEntry();
        if (running != null)
        {
            StopRunningEntry(running, DateTime.UtcNow);
            return false;
        }

        if (string.IsNullOrWhiteSpace(ActiveMatterFileRef))
        {
            return false;
        }

        var matter = _database.GetMatterByFileRef(ActiveMatterFileRef) ?? _database.CreateMatter(ActiveMatterFileRef);
        _database.CreateTimeEntry(matter.Id, DateTime.UtcNow);
        OnStateChanged();
        return true;
    }

    public void SwitchMatter(Matter matter)
    {
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var stopCommand = connection.CreateCommand();
            stopCommand.Transaction = transaction;
            stopCommand.CommandText = "UPDATE TimeEntries SET end_utc = $end_utc, updated_utc = $updated_utc WHERE end_utc IS NULL;";
            stopCommand.Parameters.AddWithValue("$end_utc", DateTime.UtcNow.ToString("o"));
            stopCommand.Parameters.AddWithValue("$updated_utc", DateTime.UtcNow.ToString("o"));
            stopCommand.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TimeEntries (matter_id, start_utc, end_utc, note, created_utc, updated_utc, manual_adjustment)
                VALUES ($matter_id, $start_utc, NULL, NULL, $created_utc, $updated_utc, 0);
                """;
            insert.Parameters.AddWithValue("$matter_id", matter.Id);
            insert.Parameters.AddWithValue("$start_utc", DateTime.UtcNow.ToString("o"));
            insert.Parameters.AddWithValue("$created_utc", DateTime.UtcNow.ToString("o"));
            insert.Parameters.AddWithValue("$updated_utc", DateTime.UtcNow.ToString("o"));
            insert.ExecuteNonQuery();
        });

        ActiveMatterFileRef = matter.FileRef;
        _settings.SetLastMatter(matter.FileRef);
        OnStateChanged();
    }

    public List<TimeEntry> GetTodayEntries() => _database.GetTodayEntries();

    public List<Matter> GetRecentMatters() => _database.GetRecentMatters(10);

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
