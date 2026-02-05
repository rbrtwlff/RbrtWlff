namespace AkteTimer.Models;

public sealed class BillingCase
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public long MatterId { get; set; }
    public BillingType BillingType { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public int TrackedMinutes { get; set; }
    public int DummyMinutes { get; set; }
    public int TotalMinutes { get; set; }
    public decimal TrackedAmount { get; set; }
    public decimal DummyAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? NoteForStaff { get; set; }
    public string? RvgSignature { get; set; }
    public decimal RvgTotal { get; set; }
    public bool RvgIsDifference { get; set; }
    public string? RvgBaseSignature { get; set; }
    public decimal RvgBaseTotal { get; set; }
    public int SelectedEntryCount { get; set; }
    public int IncludedEntryCount { get; set; }
}
