using System;
using System.IO;
using System.Linq;
using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class TimeEntryServiceTests
{
    [Fact]
    public void Pause_StopsRunningEntry()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("123/24");

        service.SwitchMatter(matter);

        service.Pause();

        var running = service.GetRunningEntry();
        var entries = service.GetEntriesForMatter(matter.Id);

        Assert.Null(running);
        Assert.Single(entries);
        Assert.NotNull(entries[0].EndUtc);
    }

    [Fact]
    public void PauseAndReturnStoppedEntry_ReturnsEntry()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("223/24");

        service.SwitchMatter(matter);

        var stopped = service.PauseAndReturnStoppedEntry();

        Assert.NotNull(stopped);
        Assert.Null(service.GetRunningEntry());
        Assert.Contains(service.GetEntriesForMatter(matter.Id), entry => entry.Id == stopped!.Id);
    }

    [Fact]
    public void StartAfterPause_CreatesNewEntry()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("234/24");

        service.SwitchMatter(matter);
        service.Pause();

        var first = service.GetEntriesForMatter(matter.Id).Single();

        service.Start();

        var running = service.GetRunningEntry();
        var entries = service.GetEntriesForMatter(matter.Id);

        Assert.NotNull(running);
        Assert.Equal(2, entries.Count);
        Assert.NotEqual(first.Id, running!.Id);
        Assert.True(running.StartUtc > first.StartUtc);
    }

    [Fact]
    public void Stop_ClearsActiveMatterAndEndsEntry()
    {
        using var fixture = new TimeEntryFixture();
        var service = fixture.Service;
        var matter = service.CreateMatter("345/24");

        service.SwitchMatter(matter);

        service.Stop();

        var running = service.GetRunningEntry();
        var entries = service.GetEntriesForMatter(matter.Id);

        Assert.Null(running);
        Assert.Null(service.ActiveMatterFileRef);
        Assert.Single(entries);
        Assert.NotNull(entries[0].EndUtc);
    }

    [Fact]
    public void Start_RequiresConfirmedMatter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
        var database = new DatabaseService(dbPath);
        database.Initialize();
        var settings = new SettingsService(database);
        settings.EnsureDefaults();
        settings.SetLastMatter("456/24");
        var service = new TimeEntryService(database, settings);

        try
        {
            Assert.False(service.IsActiveMatterConfirmed);
            Assert.False(service.Start());

            var matter = service.CreateMatter("456/24");
            service.SwitchMatter(matter);
            service.Pause();

            Assert.True(service.IsActiveMatterConfirmed);
            Assert.True(service.Start());
        }
        finally
        {
            if (File.Exists(database.DatabasePath))
            {
                File.Delete(database.DatabasePath);
            }
        }
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
