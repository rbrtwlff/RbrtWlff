namespace AkteTimer.Models;

public sealed class BillingAdjustment
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public int MinutesDelta { get; set; }
    public string? Reason { get; set; }
    public decimal AmountDelta { get; set; }
}
