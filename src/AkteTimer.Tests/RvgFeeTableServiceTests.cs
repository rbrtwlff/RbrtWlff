using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class RvgFeeTableServiceTests
{
    [Theory]
    [InlineData(65000, 1456.50)]
    [InlineData(320000, 2912.00)]
    [InlineData(500000, 3752.00)]
    [InlineData(60858, 1456.50)]
    [InlineData(300000, 2912.00)]
    [InlineData(1300000, 6552.00)]
    public void LookupFee_ReturnsExpectedOfficialValues(decimal subjectValue, decimal expectedFee)
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

    [Theory]
    [InlineData(550000, 3927.00)]
    [InlineData(550001, 4102.00)]
    [InlineData(1300000, 6552.00)]
    public void LookupFee_AppliesStatutoryProgressionAbove500k(decimal subjectValue, decimal expectedFee)
    {
        var service = new RvgFeeTableService();

        var fee = service.LookupFee1_0(subjectValue);

        Assert.Equal(expectedFee, fee);
    }
}
