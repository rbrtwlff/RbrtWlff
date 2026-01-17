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

        Assert.Equal(49.00m, fee);
    }

    [Fact]
    public void LookupFee_UsesExactMatch()
    {
        var service = new RvgFeeTableService();
        var fee = service.LookupFee1_0(1000m);

        Assert.Equal(80.00m, fee);
    }

    [Fact]
    public void LookupFee_UsesUpperBoundary()
    {
        var service = new RvgFeeTableService();
        var fee = service.LookupFee1_0(999999m);

        Assert.Equal(6084.00m, fee);
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
}
