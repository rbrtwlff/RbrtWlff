using System;

namespace AkteTimer.Models;

public sealed record RvgBillingCaseRecalcEntry(
    long BillingCaseId,
    long MatterId,
    long BatchId,
    DateTime BilledUtc,
    string? RvgSignature,
    bool RvgIsDifference,
    string? RvgBaseSignature);
