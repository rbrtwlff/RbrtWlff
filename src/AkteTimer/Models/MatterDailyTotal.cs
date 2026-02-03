namespace AkteTimer.Models;

public sealed class MatterDailyTotal
{
    public long MatterId { get; set; }
    public DateTime DayUtc { get; set; }
    public int RoundedMinutesSum { get; set; }
    public string? Fingerprint { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? CalcVersion { get; set; }
}
