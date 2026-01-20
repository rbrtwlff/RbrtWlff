namespace AkteTimer.Services;

public static class RvgCalculator
{
    public static decimal CalculateEstimate(decimal fee1_0Eur, decimal feeFactor, decimal feeModifierSum)
    {
        return RoundCurrency(fee1_0Eur * (feeFactor + feeModifierSum));
    }

    public static decimal? CalculateEffectiveHourlyRate(decimal rvgEstimateEur, decimal actualHours)
    {
        if (actualHours <= 0m)
        {
            return null;
        }

        return RoundCurrency(rvgEstimateEur / actualHours);
    }

    public static TimeSpan? CalculateBreakEvenTime(decimal rvgEstimateEur, decimal targetRateEurPerHour)
    {
        if (targetRateEurPerHour <= 0m)
        {
            return null;
        }

        var hours = rvgEstimateEur / targetRateEurPerHour;
        return TimeSpan.FromHours((double)hours);
    }

    public static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateFeeModifierSum(
        bool businessFee13Enabled,
        bool termFee12Enabled,
        bool settlementFee10Enabled,
        bool settlementFee15Enabled)
    {
        var sum = 0m;
        if (businessFee13Enabled)
        {
            sum += 1.3m;
        }

        if (termFee12Enabled)
        {
            sum += 1.2m;
        }

        if (settlementFee10Enabled)
        {
            sum += 1.0m;
        }

        if (settlementFee15Enabled)
        {
            sum += 1.5m;
        }

        return sum;
    }

    public static string FormatBreakEvenTime(TimeSpan timeSpan)
    {
        var totalHours = (int)timeSpan.TotalHours;
        return $"{totalHours:00}:{timeSpan.Minutes:00}";
    }
}

public sealed record RvgMetrics(
    decimal Fee1_0Eur,
    decimal EstimateEur,
    decimal? EffectiveHourlyRateEur,
    TimeSpan? BreakEvenTime);
