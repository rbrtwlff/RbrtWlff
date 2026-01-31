using System;
using System.IO;
using System.Linq;
using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class DatabaseServiceTests
{
    [Fact]
    public void GetUnbilledEntries_ReturnsOnlyUnbilled()
    {
        using var fixture = new DatabaseFixture();
        var database = fixture.Database;
        var matter = database.CreateMatter("789/24");

        var now = DateTime.UtcNow;
        var billedEntry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-60), "#Test");
        billedEntry = database.UpdateTimeEntry(billedEntry.Id, matter.Id, now.AddMinutes(-60), now.AddMinutes(-50), null, null);
        database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE TimeEntries SET billed = 1 WHERE id = $id;";
            command.Parameters.AddWithValue("$id", billedEntry.Id);
            command.ExecuteNonQuery();
        });

        var entryOne = database.CreateTimeEntry(matter.Id, now.AddMinutes(-40), "#Test");
        entryOne = database.UpdateTimeEntry(entryOne.Id, matter.Id, now.AddMinutes(-40), now.AddMinutes(-30), null, null);

        var entryTwo = database.CreateTimeEntry(matter.Id, now.AddMinutes(-20), "#Test");
        entryTwo = database.UpdateTimeEntry(entryTwo.Id, matter.Id, now.AddMinutes(-20), now.AddMinutes(-10), null, null);

        var runningEntry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-5), "#Test");

        var completedEntries = database.GetUnbilledEntries();
        Assert.Equal(new[] { entryOne.Id, entryTwo.Id }, completedEntries.Select(entry => entry.Id));
        Assert.All(completedEntries, entry => Assert.False(entry.Billed));
        Assert.All(completedEntries, entry => Assert.NotNull(entry.EndUtc));

        var allEntries = database.GetUnbilledEntries(false);
        Assert.Equal(new[] { entryOne.Id, entryTwo.Id, runningEntry.Id }, allEntries.Select(entry => entry.Id));
        Assert.All(allEntries, entry => Assert.False(entry.Billed));
        Assert.Contains(allEntries, entry => entry.EndUtc == null);
    }

    private sealed class DatabaseFixture : IDisposable
    {
        public DatabaseFixture()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
            Database = new DatabaseService(dbPath);
            Database.Initialize();
        }

        public DatabaseService Database { get; }

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
