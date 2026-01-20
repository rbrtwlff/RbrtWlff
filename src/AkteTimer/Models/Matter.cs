namespace AkteTimer.Models;

public sealed class Matter
{
    public long Id { get; set; }
    public string FileRef { get; set; } = string.Empty;
    public string? Title { get; set; }
    public bool IsArchived { get; set; }
    public BillingType BillingType { get; set; } = BillingType.Hourly;
    public decimal SubjectValueEur { get; set; }
    public decimal? FeeFactor { get; set; }
    public decimal? CustomFeeFactor { get; set; }
    public decimal TargetRateEurPerHour { get; set; }
    public decimal HourlyRateEurPerHour { get; set; } = 230m;
    public bool BusinessFee13Enabled { get; set; }
    public bool TermFee12Enabled { get; set; }
    public bool SettlementFee10Enabled { get; set; }
    public bool SettlementFee15Enabled { get; set; }
}
