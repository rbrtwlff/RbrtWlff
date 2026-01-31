using System;
using System.IO;
using AkteTimer.Models;
using AkteTimer.Services;
using AkteTimer.ViewModels;
using Xunit;

namespace AkteTimer.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void Refresh_SplitsTrackedMinutesByBillingType()
    {
        using var fixture = new DashboardFixture();
        var database = fixture.Database;

        var hourlyMatter = database.CreateMatter("111/24");
        var rvgMatter = database.CreateMatter("222/24");
        rvgMatter.BillingType = BillingType.Rvg;
        database.UpdateMatter(rvgMatter);

        var localStart = DateTime.Today.AddHours(9);
        var hourlyStartUtc = TimeZoneInfo.ConvertTimeToUtc(localStart);
        var hourlyEndUtc = hourlyStartUtc.AddHours(1);
        var hourlyEntry = database.CreateTimeEntry(hourlyMatter.Id, hourlyStartUtc, "#Test");
        database.UpdateTimeEntry(hourlyEntry.Id, hourlyMatter.Id, hourlyStartUtc, hourlyEndUtc, null, null);

        var rvgStartUtc = hourlyEndUtc.AddHours(1);
        var rvgEndUtc = rvgStartUtc.AddHours(2);
        var rvgEntry = database.CreateTimeEntry(rvgMatter.Id, rvgStartUtc, "#Test");
        database.UpdateTimeEntry(rvgEntry.Id, rvgMatter.Id, rvgStartUtc, rvgEndUtc, null, null);

        var viewModel = new DashboardViewModel(fixture.TimeEntryService, database)
        {
            FromDate = DateTime.Today,
            ToDate = DateTime.Today
        };

        Assert.Equal(180, viewModel.TotalTrackedMinutes);
        Assert.Equal(60, viewModel.HourlyTrackedMinutes);
        Assert.Equal(120, viewModel.RvgTrackedMinutes);
    }

    [Fact]
    public void Refresh_CalculatesRvgEfficiencyDelta()
    {
        using var fixture = new DashboardFixture();
        var database = fixture.Database;

        var rvgMatter = database.CreateMatter("333/24");
        rvgMatter.BillingType = BillingType.Rvg;
        rvgMatter.TargetRateEurPerHour = 80m;
        database.UpdateMatter(rvgMatter);

        var localStart = DateTime.Today.AddHours(8);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart);
        var endUtc = startUtc.AddHours(10);
        var entry = database.CreateTimeEntry(rvgMatter.Id, startUtc, "#Test");
        database.UpdateTimeEntry(entry.Id, rvgMatter.Id, startUtc, endUtc, null, null);

        var billedUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.Today.AddHours(17));
        var batch = database.CreateBillingBatch(billedUtc.AddMinutes(-30));
        var snapshot = new RvgBillingSnapshot
        {
            MatterId = rvgMatter.Id,
            BilledUtc = billedUtc,
            Signature = "TEST",
            Total = 1000m,
            BatchId = batch.Id
        };
        database.FinalizeBillingBatch(batch.Id, new[] { rvgMatter.Id }, new[] { snapshot }, billedUtc);

        var viewModel = new DashboardViewModel(fixture.TimeEntryService, database)
        {
            FromDate = DateTime.Today,
            ToDate = DateTime.Today
        };

        Assert.Equal(800m, viewModel.RvgHypotheticalTimeAmount);
        Assert.Equal(200m, viewModel.RvgEfficiencyDelta);
    }

    private sealed class DashboardFixture : IDisposable
    {
        public DashboardFixture()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
            Database = new DatabaseService(dbPath);
            Database.Initialize();
            Settings = new SettingsService(Database);
            Settings.EnsureDefaults();
            TimeEntryService = new TimeEntryService(Database, Settings);
        }

        public DatabaseService Database { get; }
        public SettingsService Settings { get; }
        public TimeEntryService TimeEntryService { get; }

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
