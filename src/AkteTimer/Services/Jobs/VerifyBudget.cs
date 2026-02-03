namespace AkteTimer.Services.Jobs;

public readonly record struct VerifyBudget(int DailySamples, int MatterSamples, TimeSpan MaxDuration);
