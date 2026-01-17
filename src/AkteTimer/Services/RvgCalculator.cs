namespace AkteTimer.Services;

public static class RvgCalculator
{
    public static decimal CalculateEstimate(decimal fee1_0Eur, decimal feeFactor)
    {
        return RoundCurrency(fee1_0Eur * feeFactor);
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
