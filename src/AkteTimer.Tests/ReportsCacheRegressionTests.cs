using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AkteTimer.Models;
using AkteTimer.Services;
using AkteTimer.Services.Jobs;
using AkteTimer.ViewModels;
using Xunit;

namespace AkteTimer.Tests;

public sealed class ReportsCacheRegressionTests
{
    public static IEnumerable<object[]> ExampleSizes => new[]
    {
        new object[] { ExampleSize.Small },
        new object[] { ExampleSize.Medium },
        new object[] { ExampleSize.Large }
    };

    [Theory]
    [MemberData(nameof(ExampleSizes))]
    public void ReportTotalsAndRvgMetricsMatchCachedTotals(ExampleSize size)
    {
        using var fixture = new ReportsCacheFixture();
        var data = SeedExampleData(fixture.Database, size);
        var legacy = BuildLegacyResults(fixture.Database, fixture.FeeTableService, data);
        var cached = BuildCachedResults(fixture.Database, fixture.FeeTableService, data);

        Assert.Equal(legacy.RangeTotalActualMinutes, cached.RangeTotalActualMinutes);
        Assert.Equal(legacy.RangeTotalRoundedMinutes, cached.RangeTotalRoundedMinutes);

        foreach (var matter in data.Matters)
        {
            Assert.True(legacy.ByMatter.TryGetValue(matter.Id, out var legacyTotals));
            Assert.True(cached.ByMatter.TryGetValue(matter.Id, out var cachedTotals));

            Assert.Equal(legacyTotals.RangeActualMinutes, cachedTotals.RangeActualMinutes);
            Assert.Equal(legacyTotals.RangeRoundedMinutes, cachedTotals.RangeRoundedMinutes);
            Assert.Equal(legacyTotals.AllTimeRoundedMinutes, cachedTotals.AllTimeRoundedMinutes);

            if (matter.BillingType == BillingType.Rvg)
            {
                Assert.NotNull(legacyTotals.RvgMetrics);
                Assert.NotNull(cachedTotals.RvgMetrics);
                Assert.Equal(legacyTotals.RvgMetrics!.Fee1_0Eur, cachedTotals.RvgMetrics!.Fee1_0Eur);
                Assert.Equal(legacyTotals.RvgMetrics!.EstimateEur, cachedTotals.RvgMetrics!.EstimateEur);
                Assert.Equal(legacyTotals.RvgMetrics!.EffectiveHourlyRateEur, cachedTotals.RvgMetrics!.EffectiveHourlyRateEur);
                Assert.Equal(legacyTotals.RvgMetrics!.BreakEvenTime, cachedTotals.RvgMetrics!.BreakEvenTime);
            }
            else
            {
                Assert.Null(legacyTotals.RvgMetrics);
                Assert.Null(cachedTotals.RvgMetrics);
            }
        }
    }

    [Fact]
    public void UpdatingEntryOnlyRecalculatesAffectedDailyTotalsAndKeepsReportsAccurate()
    {
        using var fixture = new ReportsCacheFixture();
        var baseDay = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeStart = baseDay;
        var rangeEnd = baseDay.AddDays(2);

        var matter = fixture.Database.CreateMatter("500/24");
        matter.BillingType = BillingType.Rvg;
        matter.SubjectValueEur = 15000m;
        matter.HourlyRateEurPerHour = 250m;
        matter.TargetRateEurPerHour = 190m;
        matter.BusinessFee13Enabled = true;
        matter.TermFee12Enabled = true;
        fixture.Database.UpdateMatter(matter);

        var day1Entry = CreateEntry(fixture.Database, matter.Id, baseDay.AddHours(9), 60);
        var day2Entry = CreateEntry(fixture.Database, matter.Id, baseDay.AddDays(1).AddHours(9), 5);
        var day3Entry = CreateEntry(fixture.Database, matter.Id, baseDay.AddDays(2).AddHours(9), 30);

        var job = new MatterTotalsJob(fixture.Database);
        job.RebuildMatter(matter.Id, CancellationToken.None);

        var day1Total = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay) ?? throw new InvalidOperationException();
        var day2Total = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay.AddDays(1)) ?? throw new InvalidOperationException();
        var day3Total = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay.AddDays(2)) ?? throw new InvalidOperationException();
        var originalMatterTotals = fixture.Database.GetMatterTotals(matter.Id) ?? throw new InvalidOperationException();

        fixture.Database.UpdateTimeEntry(
            day2Entry.Id,
            matter.Id,
            day2Entry.StartUtc,
            day2Entry.StartUtc.AddMinutes(10),
            day2Entry.Hashtag,
            day2Entry.Note);

        job.RecalcDailyTotal(matter.Id, baseDay.AddDays(1));
        job.RecalcMatterTotal(matter.Id);

        var updatedDay1 = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay) ?? throw new InvalidOperationException();
        var updatedDay2 = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay.AddDays(1)) ?? throw new InvalidOperationException();
        var updatedDay3 = fixture.Database.GetMatterDailyTotal(matter.Id, baseDay.AddDays(2)) ?? throw new InvalidOperationException();
        var updatedMatterTotals = fixture.Database.GetMatterTotals(matter.Id) ?? throw new InvalidOperationException();

        Assert.Equal(day1Total.RoundedMinutesSum, updatedDay1.RoundedMinutesSum);
        Assert.Equal(day1Total.UpdatedAtUtc, updatedDay1.UpdatedAtUtc);
        Assert.Equal(day3Total.RoundedMinutesSum, updatedDay3.RoundedMinutesSum);
        Assert.Equal(day3Total.UpdatedAtUtc, updatedDay3.UpdatedAtUtc);

        Assert.NotEqual(day2Total.RoundedMinutesSum, updatedDay2.RoundedMinutesSum);
        Assert.NotEqual(day2Total.UpdatedAtUtc, updatedDay2.UpdatedAtUtc);
        Assert.NotEqual(originalMatterTotals.TotalRoundedMinutesAllTime, updatedMatterTotals.TotalRoundedMinutesAllTime);

        var data = new ExampleData(rangeStart, rangeEnd, new List<Matter> { matter });
        var legacy = BuildLegacyResults(fixture.Database, fixture.FeeTableService, data);
        var cached = BuildCachedResults(fixture.Database, fixture.FeeTableService, data, skipRebuild: true);

        Assert.Equal(legacy.RangeTotalRoundedMinutes, cached.RangeTotalRoundedMinutes);
        Assert.Equal(legacy.ByMatter[matter.Id].RangeRoundedMinutes, cached.ByMatter[matter.Id].RangeRoundedMinutes);
        Assert.Equal(legacy.ByMatter[matter.Id].AllTimeRoundedMinutes, cached.ByMatter[matter.Id].AllTimeRoundedMinutes);
        Assert.Equal(legacy.ByMatter[matter.Id].RvgMetrics?.EstimateEur, cached.ByMatter[matter.Id].RvgMetrics?.EstimateEur);
    }

    private static ExampleData SeedExampleData(DatabaseService database, ExampleSize size)
    {
        var rangeStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = rangeStart.AddDays(6);
        var (matterCount, entriesPerMatter) = size switch
        {
            ExampleSize.Small => (2, 4),
            ExampleSize.Medium => (4, 12),
            ExampleSize.Large => (8, 30),
            _ => (2, 4)
        };

        var random = new Random(1200 + (int)size);
        var matters = new List<Matter>();

        for (var i = 0; i < matterCount; i++)
        {
            var matter = database.CreateMatter($"{100 + i}/24");
            var isRvg = i % 2 == 0;
            matter.BillingType = isRvg ? BillingType.Rvg : BillingType.Hourly;
            matter.SubjectValueEur = 10000m + (i * 1500m);
            matter.HourlyRateEurPerHour = 200m + (i * 10m);
            matter.TargetRateEurPerHour = 180m + (i * 5m);
            matter.BusinessFee13Enabled = isRvg;
            matter.TermFee12Enabled = isRvg && i % 3 == 0;
            matter.SettlementFee10Enabled = isRvg && i % 3 == 1;
            matter.SettlementFee15Enabled = isRvg && i % 3 == 2;
            matter.CustomFeeFactor = isRvg && i % 4 == 0 ? 1.8m : null;
            database.UpdateMatter(matter);
            matters.Add(matter);

            for (var j = 0; j < entriesPerMatter; j++)
            {
                var dayOffset = random.Next(0, 7);
                var hourOffset = 8 + (j % 8);
                var minuteOffset = (j * 7) % 55;
                var startUtc = rangeStart.AddDays(dayOffset).AddHours(hourOffset).AddMinutes(minuteOffset);
                var durationMinutes = 12 + (random.Next(1, 6) * 6);
                CreateEntry(database, matter.Id, startUtc, durationMinutes);
            }

            for (var j = 0; j < 2; j++)
            {
                var startUtc = rangeEnd.AddDays(1 + j).AddHours(9 + j);
                CreateEntry(database, matter.Id, startUtc, 24 + (j * 6));
            }
        }

        return new ExampleData(rangeStart, rangeEnd, matters);
    }

    private static TimeEntry CreateEntry(DatabaseService database, long matterId, DateTime startUtc, int durationMinutes)
    {
        var entry = database.CreateTimeEntry(matterId, startUtc, "#Test");
        return database.UpdateTimeEntry(entry.Id, matterId, startUtc, startUtc.AddMinutes(durationMinutes), entry.Hashtag, entry.Note);
    }

    private static ReportResults BuildLegacyResults(DatabaseService database, RvgFeeTableService feeTable, ExampleData data)
    {
        var matterIds = data.Matters.Select(matter => matter.Id).ToList();
        var entries = database.GetEntriesInRange(data.RangeStartUtc, data.RangeEndUtc.AddDays(1), matterIds);
        var entriesByMatter = entries.GroupBy(entry => entry.MatterId).ToDictionary(group => group.Key, group => group.ToList());

        var results = new Dictionary<long, MatterReportTotals>();
        var totalActualMinutes = 0;
        var totalRoundedMinutes = 0;

        foreach (var matter in data.Matters)
        {
            entriesByMatter.TryGetValue(matter.Id, out var matterEntries);
            matterEntries ??= new List<TimeEntry>();
            var rangeActual = matterEntries.Sum(entry => TimeEntryCalculations.GetActualMinutes(TimeEntryCalculations.GetDuration(entry)));
            var rangeRounded = matterEntries.Sum(entry => TimeEntryCalculations.GetRoundedMinutes(TimeEntryCalculations.GetActualMinutes(TimeEntryCalculations.GetDuration(entry))));

            var allEntries = database.GetEntriesForMatter(matter.Id);
            var allTimeRounded = allEntries.Sum(entry => TimeEntryCalculations.GetRoundedMinutes(TimeEntryCalculations.GetActualMinutes(TimeEntryCalculations.GetDuration(entry))));
            var metrics = CalculateRvgMetrics(matter, rangeActual, feeTable);

            totalActualMinutes += rangeActual;
            totalRoundedMinutes += rangeRounded;

            results[matter.Id] = new MatterReportTotals(rangeActual, rangeRounded, allTimeRounded, metrics);
        }

        return new ReportResults(totalActualMinutes, totalRoundedMinutes, results);
    }

    private static ReportResults BuildCachedResults(
        DatabaseService database,
        RvgFeeTableService feeTable,
        ExampleData data,
        bool skipRebuild = false)
    {
        if (!skipRebuild)
        {
            var job = new MatterTotalsJob(database);
            foreach (var matter in data.Matters)
            {
                job.RebuildMatter(matter.Id, CancellationToken.None);
            }
        }

        var startDay = data.RangeStartUtc.Date;
        var endDay = data.RangeEndUtc.Date;
        var matterIds = data.Matters.Select(matter => matter.Id).ToList();
        var entries = database.GetEntriesInRange(data.RangeStartUtc, data.RangeEndUtc.AddDays(1), matterIds);
        var entriesByMatter = entries.GroupBy(entry => entry.MatterId).ToDictionary(group => group.Key, group => group.ToList());

        var results = new Dictionary<long, MatterReportTotals>();
        var totalActualMinutes = 0;
        var totalRoundedMinutes = 0;

        foreach (var matter in data.Matters)
        {
            entriesByMatter.TryGetValue(matter.Id, out var matterEntries);
            matterEntries ??= new List<TimeEntry>();
            var rangeActual = matterEntries.Sum(entry => TimeEntryCalculations.GetActualMinutes(TimeEntryCalculations.GetDuration(entry)));

            var dailyTotals = database.GetMatterDailyTotalsInRange(matter.Id, startDay, endDay);
            var rangeRounded = dailyTotals.Sum(total => total.RoundedMinutesSum);

            var totals = database.GetMatterTotals(matter.Id);
            var allTimeRounded = totals?.TotalRoundedMinutesAllTime ?? 0;
            var metrics = CalculateRvgMetrics(matter, rangeActual, feeTable);

            totalActualMinutes += rangeActual;
            totalRoundedMinutes += rangeRounded;

            results[matter.Id] = new MatterReportTotals(rangeActual, rangeRounded, allTimeRounded, metrics);
        }

        return new ReportResults(totalActualMinutes, totalRoundedMinutes, results);
    }

    private static RvgMetrics? CalculateRvgMetrics(Matter matter, int actualMinutes, RvgFeeTableService feeTable)
    {
        if (matter.BillingType != BillingType.Rvg)
        {
            return null;
        }

        var breakdown = RvgCalculator.CalculateBreakdown(matter, feeTable);
        var estimate = breakdown.TotalEur;
        var actualHours = actualMinutes / 60m;
        var effective = RvgCalculator.CalculateEffectiveHourlyRate(estimate, actualHours);
        var breakEven = RvgCalculator.CalculateBreakEvenTime(estimate, matter.TargetRateEurPerHour);
        return new RvgMetrics(breakdown.Fee1_0Eur, estimate, effective, breakEven);
    }

    private enum ExampleSize
    {
        Small = 1,
        Medium = 2,
        Large = 3
    }

    private sealed record ExampleData(DateTime RangeStartUtc, DateTime RangeEndUtc, List<Matter> Matters);

    private sealed record MatterReportTotals(
        int RangeActualMinutes,
        int RangeRoundedMinutes,
        int AllTimeRoundedMinutes,
        RvgMetrics? RvgMetrics);

    private sealed record ReportResults(
        int RangeTotalActualMinutes,
        int RangeTotalRoundedMinutes,
        Dictionary<long, MatterReportTotals> ByMatter);

    private sealed class ReportsCacheFixture : IDisposable
    {
        public ReportsCacheFixture()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
            Database = new DatabaseService(dbPath);
            Database.Initialize();
            Settings = new SettingsService(Database);
            Settings.EnsureDefaults();
            FeeTableService = new RvgFeeTableService();
        }

        public DatabaseService Database { get; }
        public SettingsService Settings { get; }
        public RvgFeeTableService FeeTableService { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Database.DatabasePath))
                {
                    File.Delete(Database.DatabasePath);
                }
            }
            catch
            {
            }
        }
    }
}
