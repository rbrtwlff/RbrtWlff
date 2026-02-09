using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class RvgFeeTableServiceTests
{
    [Theory]
    [InlineData(500, 51.50m)]
    [InlineData(1000, 93.00m)]
    [InlineData(3000, 235.50m)]
    [InlineData(10000, 652.00m)]
    [InlineData(50000, 1357.00m)]
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

        var fee = service.LookupFee1_0(501m);

        Assert.Equal(93.00m, fee);
    }
}
