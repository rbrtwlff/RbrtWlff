using System.Text.RegularExpressions;
using AkteTimer.Models;
using AkteTimer.Services.Jobs;

namespace AkteTimer.Services;

public sealed class TimeEntryService
{
    private static readonly Regex FileRefRegex = new("^\\d{1,6}\\/\\d{2}$", RegexOptions.Compiled);
    public static readonly IReadOnlyList<string> DefaultHashtags = new[]
    {
        "#Schriftsatz",
        "#E-Mail",
        "#Telefon",
        "#Prüfung",
        "#Termin",
        "#Besprechung",
        "#Organisation",
        "#Sonstiges"
    };

    private readonly DatabaseService _database;
    private readonly SettingsService _settings;
    private readonly MatterTotalsQueue? _matterTotalsQueue;
    private readonly MatterTotalsVerifyQueue? _verifyQueue;

    public TimeEntryService(
        DatabaseService database,
        SettingsService settings,
        MatterTotalsQueue? matterTotalsQueue = null,
        MatterTotalsVerifyQueue? verifyQueue = null)
    {
        _database = database;
        _settings = settings;
        _matterTotalsQueue = matterTotalsQueue;
        _verifyQueue = verifyQueue;
        ActiveMatterFileRef = _settings.LastMatter;
        IsActiveMatterConfirmed = false;
    }

    public event EventHandler? StateChanged;

    public string? ActiveMatterFileRef { get; private set; }

    public bool IsActiveMatterConfirmed { get; private set; }

    public bool IsRunning => GetRunningEntry() != null;

    public bool IsValidFileRef(string fileRef) => FileRefRegex.IsMatch(fileRef);

    public TimeEntry? GetRunningEntry() => _database.GetRunningEntry();

    public Matter? GetMatterByFileRef(string fileRef) => _database.GetMatterByFileRef(fileRef);

    public Matter CreateMatter(string fileRef) => _database.CreateMatter(fileRef);

    public void ResumeRunningEntry(TimeEntry entry)
    {
        ActiveMatterFileRef = entry.MatterFileRef;
        IsActiveMatterConfirmed = true;
        if (!string.IsNullOrWhiteSpace(entry.MatterFileRef))
        {
            _settings.SetLastMatter(entry.MatterFileRef);
        }

        if (!string.IsNullOrWhiteSpace(entry.Hashtag))
        {
            _settings.SetLastHashtag(entry.Hashtag);
        }
        OnStateChanged();
    }

    public void Pause()
    {
        var running = GetRunningEntry();
        if (running == null)
        {
            return;
        }

        StopRunningEntries(DateTime.UtcNow);
    }

    public TimeEntry? PauseAndReturnStoppedEntry()
    {
        var running = GetRunningEntry();
        if (running == null)
        {
            return null;
        }

        StopRunningEntries(DateTime.UtcNow);
        return running;
    }

    public bool Start()
    {
        if (GetRunningEntry() != null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ActiveMatterFileRef) || !IsActiveMatterConfirmed)
        {
            return false;
        }

        var matter = _database.GetMatterByFileRef(ActiveMatterFileRef) ?? _database.CreateMatter(ActiveMatterFileRef);
        var entry = _database.CreateTimeEntry(matter.Id, DateTime.UtcNow, _settings.GetStartHashtag());
        EnqueueTotalsForEntry(entry);
        OnStateChanged();
        return true;
    }

    public void Stop()
    {
        if (GetRunningEntry() != null)
        {
            StopRunningEntries(DateTime.UtcNow);
        }

        ActiveMatterFileRef = null;
        IsActiveMatterConfirmed = false;
        OnStateChanged();
    }

    public void StopRunningEntries(DateTime endUtc)
    {
        var runningEntry = GetRunningEntry();
        _database.StopRunningEntries(endUtc);
        if (runningEntry != null)
        {
            EnqueueTotalsForEntry(runningEntry, endUtc);
        }
        OnStateChanged();
    }

    public bool ToggleStartPause()
    {
        var running = GetRunningEntry();
        if (running != null)
        {
            Pause();
            return false;
        }

        return Start();
    }

    public void SwitchMatter(Matter matter)
    {
        var now = DateTime.UtcNow;
        var runningEntry = GetRunningEntry();
        _database.ExecuteInTransaction((connection, transaction) =>
        {
            using var stopCommand = connection.CreateCommand();
            stopCommand.Transaction = transaction;
            stopCommand.CommandText = "UPDATE TimeEntries SET end_utc = $end_utc, updated_utc = $updated_utc WHERE end_utc IS NULL;";
            stopCommand.Parameters.AddWithValue("$end_utc", now.ToString("o"));
            stopCommand.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
            stopCommand.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TimeEntries (matter_id, start_utc, end_utc, note, hashtag, created_utc, updated_utc, manual_adjustment)
                VALUES ($matter_id, $start_utc, NULL, NULL, $hashtag, $created_utc, $updated_utc, 0);
                """;
            insert.Parameters.AddWithValue("$matter_id", matter.Id);
            insert.Parameters.AddWithValue("$start_utc", now.ToString("o"));
            insert.Parameters.AddWithValue("$hashtag", _settings.GetStartHashtag());
            insert.Parameters.AddWithValue("$created_utc", now.ToString("o"));
            insert.Parameters.AddWithValue("$updated_utc", now.ToString("o"));
            insert.ExecuteNonQuery();
        });

        ActiveMatterFileRef = matter.FileRef;
        _settings.SetLastMatter(matter.FileRef);
        IsActiveMatterConfirmed = true;
        if (runningEntry != null)
        {
            EnqueueTotalsForEntry(runningEntry, now);
        }

        var newEntry = GetRunningEntry();
        if (newEntry != null)
        {
            EnqueueTotalsForEntry(newEntry);
        }
        OnStateChanged();
    }

    public List<TimeEntry> GetTodayEntries() => _database.GetTodayEntries();

    public List<TimeEntry> GetEntriesInRange(DateTime startLocal, DateTime endLocal, IReadOnlyCollection<long> matterIds)
    {
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal.Date);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal.Date.AddDays(1));
        return _database.GetEntriesInRange(startUtc, endUtc, matterIds);
    }

    public List<TimeEntry> GetEntriesForMatter(long matterId) => _database.GetEntriesForMatter(matterId);

    public List<Matter> GetAllMatters() => _database.GetAllMatters();

    public List<Matter> GetRecentMatters() => _database.GetRecentMatters(10);

    public string GetDefaultHashtag() => _settings.GetStartHashtag();

    public bool ShouldPromptForHashtag() => _settings.IsHashtagStopPromptEnabled;

    public decimal GetEffectiveTargetRate(decimal matterRate)
    {
        return matterRate > 0m ? matterRate : _settings.GlobalTargetRateEurPerHour;
    }

    public decimal GetEffectiveTargetRate(Matter matter) => GetEffectiveTargetRate(matter.TargetRateEurPerHour);

    public void SetEntryHashtag(long entryId, string hashtag)
    {
        if (string.IsNullOrWhiteSpace(hashtag))
        {
            return;
        }

        _database.UpdateTimeEntryHashtag(entryId, hashtag);
        _settings.SetLastHashtag(hashtag);
    }

    public void UpdateTimeEntryNote(long entryId, string? note)
    {
        _database.UpdateTimeEntryNote(entryId, note);
        OnStateChanged();
    }

    public void UpdateTimeEntry(long entryId, string matterFileRef, DateTime startLocal, DateTime endLocal, string? hashtag, string? note)
    {
        if (endLocal < startLocal)
        {
            throw new InvalidOperationException("Ende darf nicht vor Start liegen.");
        }

        if (!IsValidFileRef(matterFileRef))
        {
            throw new InvalidOperationException("Akte hat ein ungültiges Format.");
        }

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal);
        if (endUtc < startUtc)
        {
            throw new InvalidOperationException("Ende darf nicht vor Start liegen.");
        }

        var matter = _database.GetMatterByFileRef(matterFileRef) ?? _database.CreateMatter(matterFileRef);
        var existingEntry = _database.GetTimeEntryById(entryId);
        var updatedEntry = _database.UpdateTimeEntry(entryId, matter.Id, startUtc, endUtc, hashtag, note);
        if (existingEntry != null)
        {
            EnqueueTotalsForEntry(existingEntry);
        }

        EnqueueTotalsForEntry(updatedEntry);
        OnStateChanged();
    }

    public void SplitTimeEntry(long entryId, DateTime splitLocal)
    {
        var entry = _database.GetTimeEntryById(entryId);
        if (entry == null)
        {
            throw new InvalidOperationException("Eintrag nicht gefunden.");
        }

        if (entry.EndUtc == null)
        {
            throw new InvalidOperationException("Laufende Einträge können nicht gesplittet werden.");
        }

        var startLocal = entry.StartUtc.ToLocalTime();
        var endLocal = entry.EndUtc.Value.ToLocalTime();
        if (splitLocal <= startLocal || splitLocal >= endLocal)
        {
            throw new InvalidOperationException("Split-Zeit muss zwischen Start und Ende liegen.");
        }

        var splitUtc = TimeZoneInfo.ConvertTimeToUtc(splitLocal);
        if (splitUtc <= entry.StartUtc || splitUtc >= entry.EndUtc)
        {
            throw new InvalidOperationException("Split-Zeit muss zwischen Start und Ende liegen.");
        }

        var (updatedEntry, newEntry) = _database.SplitTimeEntry(entryId, splitUtc);
        EnqueueTotalsForEntry(updatedEntry);
        EnqueueTotalsForEntry(newEntry);
        OnStateChanged();
    }

    public void DeleteTimeEntry(long entryId)
    {
        var entry = _database.GetTimeEntryById(entryId);
        if (entry == null)
        {
            return;
        }

        if (entry.EndUtc == null)
        {
            throw new InvalidOperationException("Laufende Einträge können nicht gelöscht werden.");
        }

        _database.DeleteTimeEntry(entryId);
        EnqueueTotalsForEntry(entry);
        OnStateChanged();
    }

    public void UpdateMatter(Matter matter)
    {
        _database.UpdateMatter(matter);
        OnStateChanged();
    }

    public void EnqueueMatterTotalsRefresh(long matterId)
    {
        _matterTotalsQueue?.EnqueueMatterTotal(matterId);
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        _verifyQueue?.NotifyIdle(IsRunning);
    }

    private void EnqueueTotalsForEntry(TimeEntry entry, DateTime? endOverrideUtc = null)
    {
        _matterTotalsQueue?.EnqueueForEntry(entry, endOverrideUtc);
    }
}
