using System;
using System.IO;
using System.Linq;
using AkteTimer.Models;
using AkteTimer.Services;
using AkteTimer.ViewModels;
using Xunit;

namespace AkteTimer.Tests;

public sealed class ReportEntryViewModelTests
{
    [Fact]
    public void SubjectValue_WhenSetToPositive_SetsBillingTypeToRvg()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("123/24");

        service.SwitchMatter(matter);
        service.Pause();

        var entry = service.GetEntriesForMatter(matter.Id).Single();
        var viewModel = new ReportEntryViewModel(entry, matter, service, (_, _) => { });

        viewModel.SubjectValueEur = 10000m;

        Assert.Equal(BillingType.Rvg, matter.BillingType);
        Assert.Equal(BillingType.Rvg, viewModel.BillingType);
    }

    [Fact]
    public void SubjectValue_WhenSetToZero_DoesNotResetBillingType()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("234/24");
        matter.BillingType = BillingType.Rvg;
        matter.SubjectValueEur = 10000m;
        service.UpdateMatter(matter);

        service.SwitchMatter(matter);
        service.Pause();

        var entry = service.GetEntriesForMatter(matter.Id).Single();
        var viewModel = new ReportEntryViewModel(entry, matter, service, (_, _) => { });

        viewModel.SubjectValueEur = 0m;

        Assert.Equal(BillingType.Rvg, matter.BillingType);
        Assert.Equal(BillingType.Rvg, viewModel.BillingType);
    }

    private sealed class TimeEntryFixture : IDisposable
    {
        public TimeEntryFixture()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
            Database = new DatabaseService(dbPath);
            Database.Initialize();
            Settings = new SettingsService(Database);
            Settings.EnsureDefaults();
            Service = new TimeEntryService(Database, Settings);
        }

        public DatabaseService Database { get; }
        public SettingsService Settings { get; }
        public TimeEntryService Service { get; }

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
