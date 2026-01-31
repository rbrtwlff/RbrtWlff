namespace AkteTimer.Models;

public sealed class BillingBatch
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? FinalizedUtc { get; set; }
    public string? PdfPath { get; set; }
}
