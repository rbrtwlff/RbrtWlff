namespace AkteTimer.Models;

public static class BillingTypeRules
{
    public static BillingType RecomputeBillingType(Matter matter)
    {
        if (matter.SubjectValueEur <= 0m)
        {
            return BillingType.Hourly;
        }

        var hasCustomFee = matter.CustomFeeFactor.HasValue && matter.CustomFeeFactor.Value > 0m;
        var hasEnabledToggle = matter.BusinessFee13Enabled
            || matter.TermFee12Enabled
            || matter.SettlementFee10Enabled
            || matter.SettlementFee15Enabled;

        return hasCustomFee || hasEnabledToggle
            ? BillingType.Rvg
            : BillingType.Hourly;
    }
}
