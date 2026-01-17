namespace AkteTimer.Models;

public sealed class TimeEntry
{
    public long Id { get; set; }
    public long MatterId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool ManualAdjustment { get; set; }
    public string? MatterFileRef { get; set; }
    public string? Hashtag { get; set; }
}
