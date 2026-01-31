using System.Linq;
using AkteTimer.Models;

namespace AkteTimer.Services;

public static class RvgCalculator
{
    public static RvgBreakdown CalculateBreakdown(Matter matter, RvgFeeTableService tableService)
    {
        var baseFee = tableService.LookupFee1_0(matter.SubjectValueEur);
        var items = new List<RvgLineItem>();

        if (matter.BusinessFee13Enabled)
        {
            items.Add(CreateLineItem("Geschäftsgebühr", 1.3m, baseFee));
        }

        if (matter.TermFee12Enabled)
        {
            items.Add(CreateLineItem("Terminsgebühr", 1.2m, baseFee));
        }

        if (matter.SettlementFee10Enabled)
        {
            items.Add(CreateLineItem("Einigungsgebühr", 1.0m, baseFee));
        }

        if (matter.SettlementFee15Enabled)
        {
            items.Add(CreateLineItem("Einigungsgebühr", 1.5m, baseFee));
        }

        if (matter.CustomFeeFactor.HasValue)
        {
            items.Add(CreateLineItem("Gebühr (Custom)", matter.CustomFeeFactor.Value, baseFee));
        }

        var total = RoundCurrency(items.Sum(item => item.Amount));
        return new RvgBreakdown(items, total);
    }

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

    private static RvgLineItem CreateLineItem(string name, decimal factor, decimal baseFee)
    {
        var amount = RoundCurrency(baseFee * factor);
        return new RvgLineItem(name, factor, baseFee, amount);
    }
}

public sealed record RvgMetrics(
    decimal Fee1_0Eur,
    decimal EstimateEur,
    decimal? EffectiveHourlyRateEur,
    TimeSpan? BreakEvenTime);
