using System;
using System.IO;
using System.Linq;
using AkteTimer.Models;
using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class RvgRecalculateFlowTests
{
    [Fact]
    public void RecalculateUpdatesSnapshotsAndTotals()
    {
        using var fixture = new DatabaseFixture();
        var database = fixture.Database;
        var billingService = new BillingService(database);
        var feeTable = new RvgFeeTableService();

        var matter = database.CreateMatter("999/25");
        matter.BillingType = BillingType.Rvg;
        matter.SubjectValueEur = 1000m;
        matter.BusinessFee13Enabled = true;
        database.UpdateMatter(matter);

        var now = DateTime.UtcNow;
        var entry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-60), "#Test");
        entry = database.UpdateTimeEntry(entry.Id, matter.Id, now.AddMinutes(-60), now.AddMinutes(-30), null, null);

        var batch = database.CreateBillingBatch(now);
        var billingCase = new BillingCase
        {
            BatchId = batch.Id,
            MatterId = matter.Id,
            BillingType = BillingType.Rvg,
            ApprovedUtc = null,
            TrackedMinutes = 30,
            DummyMinutes = 0,
            TotalMinutes = 30,
            TrackedAmount = 0m,
            DummyAmount = 0m,
            TotalAmount = 0m,
            NoteForStaff = null,
            RvgSignature = null,
            RvgTotal = 0m,
            RvgIsDifference = false,
            RvgBaseSignature = null,
            RvgBaseTotal = 0m,
            SelectedEntryCount = 1,
            IncludedEntryCount = 1
        };
        var createdCase = database.CreateBillingCase(billingCase);

        var signature = BillingService.ComputeRvgSignature(matter);
        var breakdown = RvgCalculator.CalculateBreakdown(matter, feeTable);
        database.UpdateBillingCaseRvgData(
            createdCase.Id,
            signature,
            breakdown.Total,
            false,
            null,
            0m);

        database.UpdateBillingBatchPdfPath(batch.Id, Path.Combine(Path.GetTempPath(), "test.pdf"));
        billingService.FinalizeBatch(batch.Id);

        var snapshot = database.GetRvgBillingSnapshotForBatch(batch.Id, matter.Id);
        Assert.NotNull(snapshot);

        database.UpdateRvgBillingSnapshot(snapshot!.Id, signature, 1m, null);

        billingService.RecalculateRvgSnapshotsForAll();

        var recalculatedSnapshot = database.GetRvgBillingSnapshotForBatch(batch.Id, matter.Id);
        Assert.NotNull(recalculatedSnapshot);
        Assert.Equal(breakdown.Total, recalculatedSnapshot!.Total);

        var updatedCase = database.GetBillingCasesForBatch(batch.Id).Single();
        Assert.Equal(breakdown.Total, updatedCase.RvgTotal);
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
