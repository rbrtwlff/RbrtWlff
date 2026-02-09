using AkteTimer.Models;
using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class RvgCalculatorTests
{
    [Fact]
    public void LookupFee_UsesLowerBoundary()
    {
        var service = new RvgFeeTableService();
        var fee = service.LookupFee1_0(1m);

        Assert.Equal(51.50m, fee);
    }

    [Fact]
    public void LookupFee_UsesExactMatch()
    {
        var service = new RvgFeeTableService();
        var fee = service.LookupFee1_0(1000m);

        Assert.Equal(93.00m, fee);
    }

    [Fact]
    public void LookupFee_UsesUpperBoundary()
    {
        var service = new RvgFeeTableService();
        var fee = service.LookupFee1_0(999999m);

        Assert.Equal(4755.00m, fee);
    }

    [Fact]
    public void Estimate_MultipliesFactor()
    {
        var estimate = RvgCalculator.CalculateEstimate(100m, 1.3m);

        Assert.Equal(130.00m, estimate);
    }

    [Fact]
    public void Estimate_RoundsToTwoDecimals()
    {
        var estimate = RvgCalculator.CalculateEstimate(10.005m, 1m);

        Assert.Equal(10.01m, estimate);
    }

    [Fact]
    public void CalculateBreakdown_TotalMatchesLegacyCalculation()
    {
        var service = new RvgFeeTableService();
        var matter = new Matter
        {
            BillingType = BillingType.Rvg,
            SubjectValueEur = 500m,
            BusinessFee13Enabled = true,
            TermFee12Enabled = true,
            SettlementFee10Enabled = true,
            SettlementFee15Enabled = false,
            CustomFeeFactor = 1.7m
        };

        var fee1_0 = service.LookupFee1_0(matter.SubjectValueEur);
        var businessFee = RvgCalculator.RoundCurrency(fee1_0 * 1.3m);
        var termFee = RvgCalculator.RoundCurrency(fee1_0 * 1.2m);
        var settlementFee = RvgCalculator.RoundCurrency(fee1_0 * 1.0m);
        var customFee = RvgCalculator.RoundCurrency(fee1_0 * 1.7m);
        var expectedTotal = RvgCalculator.RoundCurrency(businessFee + termFee + settlementFee + customFee);

        var breakdown = RvgCalculator.CalculateBreakdown(matter, service);

        Assert.Equal(expectedTotal, breakdown.Total);
    }

    [Fact]
    public void CalculateBreakdown_BuildsBusinessFeeLineItem()
    {
        var service = new RvgFeeTableService();
        var matter = new Matter
        {
            BillingType = BillingType.Rvg,
            SubjectValueEur = 500m,
            BusinessFee13Enabled = true
        };

        var fee1_0 = service.LookupFee1_0(matter.SubjectValueEur);
        var breakdown = RvgCalculator.CalculateBreakdown(matter, service);

        Assert.Single(breakdown.Items);
        Assert.Equal("Geschäftsgebühr", breakdown.Items[0].Name);
        Assert.Equal(1.3m, breakdown.Items[0].Factor);
        Assert.Equal(fee1_0, breakdown.Items[0].BaseFee);
        Assert.Equal(RvgCalculator.RoundCurrency(fee1_0 * 1.3m), breakdown.Items[0].Amount);
    }
}
