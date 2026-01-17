using AkteTimer.Models;

namespace AkteTimer.ViewModels;

public static class TimeEntryCalculations
{
    public static TimeSpan GetDuration(TimeEntry entry, DateTime? nowUtc = null)
    {
        var end = entry.EndUtc ?? nowUtc ?? DateTime.UtcNow;
        return end - entry.StartUtc;
    }

    public static int GetActualMinutes(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(duration.TotalMinutes);
    }

    public static int GetRoundedMinutes(int actualMinutes)
    {
        if (actualMinutes <= 0)
        {
            return 0;
        }

        return (int)(Math.Ceiling(actualMinutes / 6d) * 6);
    }
}
