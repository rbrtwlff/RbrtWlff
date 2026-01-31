using System;

namespace AkteTimer.Models;

public sealed class RvgBillingSnapshot
{
    public long Id { get; set; }
    public long MatterId { get; set; }
    public DateTime BilledUtc { get; set; }
    public string Signature { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long BatchId { get; set; }
    public string? BreakdownJson { get; set; }
}
