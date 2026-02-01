using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class RvgFeeTableServiceTests
{
    [Theory]
    [InlineData(500, 51.50m)]
    [InlineData(2000, 149.00m)]
    [InlineData(10000, 644.50m)]
    [InlineData(50000, 1820.50m)]
    public void LookupFee_ReturnsExpectedFixpoints(decimal subjectValue, decimal expectedFee)
    {
        var service = new RvgFeeTableService();

        var fee = service.LookupFee1_0(subjectValue);

        Assert.Equal(expectedFee, fee);
    }

    [Fact]
    public void LookupFee_UsesUpperBracketForIntermediateValue()
    {
        var service = new RvgFeeTableService();

        var fee = service.LookupFee1_0(7500m);

        Assert.Equal(521.00m, fee);
    }
}
