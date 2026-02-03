namespace AkteTimer.Models;

public sealed class MatterTotals
{
    public long MatterId { get; set; }
    public int TotalRoundedMinutesAllTime { get; set; }
    public string? DailyTotalsMaxUpdatedAt { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public string? CalcVersion { get; set; }
}
