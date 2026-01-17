using AkteTimer.Models;

namespace AkteTimer.ViewModels;

public sealed class EditTimeEntryViewModel : ViewModelBase
{
    private string _matterFileRef;
    private string _hashtag;
    private string _note;
    private DateTime _startDate;
    private string _startTimeText;
    private DateTime _endDate;
    private string _endTimeText;
    private DateTime _splitDate;
    private string _splitTimeText;

    public EditTimeEntryViewModel(TimeEntry entry, IReadOnlyList<Matter> matters, IReadOnlyList<string> hashtags)
    {
        EntryId = entry.Id;
        MatterOptions = matters.Select(matter => matter.FileRef).ToList();
        HashtagOptions = hashtags.ToList();

        _matterFileRef = entry.MatterFileRef ?? string.Empty;
        _hashtag = entry.Hashtag ?? string.Empty;
        _note = entry.Note ?? string.Empty;

        var startLocal = entry.StartUtc.ToLocalTime();
        var endLocal = (entry.EndUtc ?? DateTime.UtcNow).ToLocalTime();
        _startDate = startLocal.Date;
        _startTimeText = startLocal.ToString("HH:mm");
        _endDate = endLocal.Date;
        _endTimeText = endLocal.ToString("HH:mm");

        var duration = endLocal - startLocal;
        var splitLocal = duration > TimeSpan.Zero
            ? startLocal + TimeSpan.FromMinutes(duration.TotalMinutes / 2)
            : startLocal;
        _splitDate = splitLocal.Date;
        _splitTimeText = splitLocal.ToString("HH:mm");
    }

    public long EntryId { get; }

    public IReadOnlyList<string> MatterOptions { get; }

    public IReadOnlyList<string> HashtagOptions { get; }

    public string MatterFileRef
    {
        get => _matterFileRef;
        set
        {
            if (_matterFileRef == value)
            {
                return;
            }

            _matterFileRef = value;
            NotifyPropertyChanged();
        }
    }

    public string Hashtag
    {
        get => _hashtag;
        set
        {
            if (_hashtag == value)
            {
                return;
            }

            _hashtag = value;
            NotifyPropertyChanged();
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            if (_note == value)
            {
                return;
            }

            _note = value;
            NotifyPropertyChanged();
        }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (_startDate == value)
            {
                return;
            }

            _startDate = value.Date;
            NotifyPropertyChanged();
        }
    }

    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (_startTimeText == value)
            {
                return;
            }

            _startTimeText = value;
            NotifyPropertyChanged();
        }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (_endDate == value)
            {
                return;
            }

            _endDate = value.Date;
            NotifyPropertyChanged();
        }
    }

    public string EndTimeText
    {
        get => _endTimeText;
        set
        {
            if (_endTimeText == value)
            {
                return;
            }

            _endTimeText = value;
            NotifyPropertyChanged();
        }
    }

    public DateTime SplitDate
    {
        get => _splitDate;
        set
        {
            if (_splitDate == value)
            {
                return;
            }

            _splitDate = value.Date;
            NotifyPropertyChanged();
        }
    }

    public string SplitTimeText
    {
        get => _splitTimeText;
        set
        {
            if (_splitTimeText == value)
            {
                return;
            }

            _splitTimeText = value;
            NotifyPropertyChanged();
        }
    }

    public bool TryGetStartEndLocal(out DateTime startLocal, out DateTime endLocal, out string? error)
    {
        if (!TryParseLocalTime(StartDate, StartTimeText, out startLocal))
        {
            error = "Ungültige Startzeit. Bitte HH:mm eingeben.";
            endLocal = default;
            return false;
        }

        if (!TryParseLocalTime(EndDate, EndTimeText, out endLocal))
        {
            error = "Ungültige Endzeit. Bitte HH:mm eingeben.";
            return false;
        }

        error = null;
        return true;
    }

    public bool TryGetSplitLocal(out DateTime splitLocal, out string? error)
    {
        if (!TryParseLocalTime(SplitDate, SplitTimeText, out splitLocal))
        {
            error = "Ungültige Split-Zeit. Bitte HH:mm eingeben.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseLocalTime(DateTime date, string timeText, out DateTime localDateTime)
    {
        localDateTime = default;
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return false;
        }

        if (!TimeSpan.TryParse(timeText.Trim(), out var time))
        {
            return false;
        }

        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            return false;
        }

        localDateTime = date.Date + time;
        return true;
    }
}
