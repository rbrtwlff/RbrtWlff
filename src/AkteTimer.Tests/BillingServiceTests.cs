using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkteTimer.Models;
using AkteTimer.Services;
using Xunit;

namespace AkteTimer.Tests;

public sealed class BillingServiceTests
{
    [Fact]
    public void ComputeRvgSignature_BuildsStableSignature()
    {
        var matter = new Matter
        {
            SubjectValueEur = 100m,
            CustomFeeFactor = null,
            BusinessFee13Enabled = true,
            TermFee12Enabled = false,
            SettlementFee10Enabled = true,
            SettlementFee15Enabled = false
        };

        var signature = BillingService.ComputeRvgSignature(matter);

        Assert.Equal("SV=100;CF=null;B13=1;T12=0;S10=1;S15=0", signature);
    }

    [Fact]
    public void ComputeRvgSignature_ChangesWhenTermFeeToggles()
    {
        var matter = new Matter
        {
            SubjectValueEur = 100m,
            CustomFeeFactor = 1.5m,
            BusinessFee13Enabled = true,
            TermFee12Enabled = false,
            SettlementFee10Enabled = true,
            SettlementFee15Enabled = false
        };

        var withoutTermFee = BillingService.ComputeRvgSignature(matter);

        matter.TermFee12Enabled = true;

        var withTermFee = BillingService.ComputeRvgSignature(matter);

        Assert.NotEqual(withoutTermFee, withTermFee);
    }

    [Fact]
    public void CreateBillingBatchDraft_CreatesBatchAndCasesWithRoundedMinutes()
    {
        using var fixture = new BillingFixture();
        var database = fixture.Database;
        var billingService = fixture.Service;

        var matterOne = database.CreateMatter("111/24");
        var matterTwo = database.CreateMatter("222/24");

        var now = DateTime.UtcNow;
        var entryOne = database.CreateTimeEntry(matterOne.Id, now.AddMinutes(-30), "#Test");
        entryOne = database.UpdateTimeEntry(entryOne.Id, matterOne.Id, now.AddMinutes(-30), now.AddMinutes(-20), null, null);

        var entryBilled = database.CreateTimeEntry(matterOne.Id, now.AddMinutes(-20), "#Test");
        entryBilled = database.UpdateTimeEntry(entryBilled.Id, matterOne.Id, now.AddMinutes(-20), now.AddMinutes(-10), null, null);
        database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE TimeEntries SET billed = 1, billed_utc = $billed_utc, billing_batch_id = $batch_id WHERE id = $id;";
            command.Parameters.AddWithValue("$billed_utc", now.ToString("o"));
            command.Parameters.AddWithValue("$batch_id", 99);
            command.Parameters.AddWithValue("$id", entryBilled.Id);
            command.ExecuteNonQuery();
        });

        var entryTwo = database.CreateTimeEntry(matterTwo.Id, now.AddMinutes(-10), "#Test");
        entryTwo = database.UpdateTimeEntry(entryTwo.Id, matterTwo.Id, now.AddMinutes(-10), now.AddMinutes(-5), null, null);

        var entryIds = new[] { entryOne.Id, entryBilled.Id, entryTwo.Id };

        var result = billingService.CreateBillingBatchDraft(entryIds);

        var batch = database.GetBillingBatchById(result.BatchId);
        var cases = database.GetBillingCasesForBatch(result.BatchId);

        Assert.NotNull(batch);
        Assert.Equal(2, cases.Count);
        Assert.Equal(2, result.CaseIds.Count);

        var caseByMatter = cases.ToDictionary(c => c.MatterId, c => c);
        Assert.Equal(12, caseByMatter[matterOne.Id].TrackedMinutes);
        Assert.Equal(6, caseByMatter[matterTwo.Id].TrackedMinutes);
        Assert.Equal(46m, caseByMatter[matterOne.Id].TrackedAmount);
        Assert.Equal(23m, caseByMatter[matterTwo.Id].TrackedAmount);
        Assert.Equal(1, caseByMatter[matterOne.Id].SelectedEntryCount);
        Assert.Equal(1, caseByMatter[matterOne.Id].IncludedEntryCount);
        Assert.Equal(1, caseByMatter[matterTwo.Id].SelectedEntryCount);
        Assert.Equal(1, caseByMatter[matterTwo.Id].IncludedEntryCount);

        var fileRefLookup = new Dictionary<long, string>
        {
            [matterOne.Id] = matterOne.FileRef,
            [matterTwo.Id] = matterTwo.FileRef
        };
        var expectedCaseIds = cases
            .OrderBy(item => fileRefLookup[item.MatterId], StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .ToList();

        Assert.Equal(expectedCaseIds, result.CaseIds);
    }

    [Fact]
    public void CreateBillingBatchDraft_ExpandsMatterToAllBillableEntries()
    {
        using var fixture = new BillingFixture();
        var database = fixture.Database;
        var billingService = fixture.Service;

        var matter = database.CreateMatter("111/24");
        var otherMatter = database.CreateMatter("222/24");
        var now = DateTime.UtcNow;

        var selectedEntry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-70), "#Test");
        selectedEntry = database.UpdateTimeEntry(selectedEntry.Id, matter.Id, now.AddMinutes(-70), now.AddMinutes(-60), null, null);

        var autoIncludedEntry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-55), "#Test");
        autoIncludedEntry = database.UpdateTimeEntry(autoIncludedEntry.Id, matter.Id, now.AddMinutes(-55), now.AddMinutes(-40), null, null);

        var runningEntry = database.CreateTimeEntry(matter.Id, now.AddMinutes(-5), "#Test");

        var otherEntry = database.CreateTimeEntry(otherMatter.Id, now.AddMinutes(-30), "#Test");
        otherEntry = database.UpdateTimeEntry(otherEntry.Id, otherMatter.Id, now.AddMinutes(-30), now.AddMinutes(-20), null, null);

        var result = billingService.CreateBillingBatchDraft(new[] { selectedEntry.Id });
        var billingCase = database.GetBillingCasesForBatch(result.BatchId).Single();

        Assert.Equal(matter.Id, billingCase.MatterId);
        Assert.Equal(30, billingCase.TrackedMinutes);
        Assert.Equal(115m, billingCase.TrackedAmount);
        Assert.Equal(1, billingCase.SelectedEntryCount);
        Assert.Equal(2, billingCase.IncludedEntryCount);

        database.UpdateBillingBatchPdfPath(result.BatchId, "export.pdf");
        billingService.FinalizeBatch(result.BatchId);

        Assert.True(database.GetTimeEntryById(selectedEntry.Id)!.Billed);
        Assert.True(database.GetTimeEntryById(autoIncludedEntry.Id)!.Billed);
        Assert.False(database.GetTimeEntryById(runningEntry.Id)!.Billed);
        Assert.False(database.GetTimeEntryById(otherEntry.Id)!.Billed);
    }

    [Fact]
    public void FinalizeBatch_MarksEntriesWritesSnapshotsAndFinalizesBatch()
    {
        using var fixture = new BillingFixture();
        var database = fixture.Database;
        var billingService = fixture.Service;

        var matterHourly = database.CreateMatter("111/24");
        var matterRvg = database.CreateMatter("222/24");
        matterRvg.BillingType = BillingType.Rvg;
        matterRvg.SubjectValueEur = 500m;
        matterRvg.BusinessFee13Enabled = true;
        database.UpdateMatter(matterRvg);

        var now = DateTime.UtcNow;
        var hourlyEntry = database.CreateTimeEntry(matterHourly.Id, now.AddMinutes(-30), "#Test");
        hourlyEntry = database.UpdateTimeEntry(hourlyEntry.Id, matterHourly.Id, now.AddMinutes(-30), now.AddMinutes(-20), null, null);

        var rvgEntry = database.CreateTimeEntry(matterRvg.Id, now.AddMinutes(-20), "#Test");
        rvgEntry = database.UpdateTimeEntry(rvgEntry.Id, matterRvg.Id, now.AddMinutes(-20), now.AddMinutes(-10), null, null);

        var ignoredMatter = database.CreateMatter("333/24");
        var ignoredEntry = database.CreateTimeEntry(ignoredMatter.Id, now.AddMinutes(-10), "#Test");
        ignoredEntry = database.UpdateTimeEntry(ignoredEntry.Id, ignoredMatter.Id, now.AddMinutes(-10), now.AddMinutes(-5), null, null);

        var batch = billingService.CreateBillingBatchDraft(new[] { hourlyEntry.Id, rvgEntry.Id });
        var cases = database.GetBillingCasesForBatch(batch.BatchId);
        var rvgCase = cases.Single(billingCase => billingCase.MatterId == matterRvg.Id);

        database.UpdateBillingCaseRvgData(
            rvgCase.Id,
            "SIG-1",
            250m,
            false,
            null,
            0m);

        database.UpdateBillingBatchPdfPath(batch.BatchId, "export.pdf");

        billingService.FinalizeBatch(batch.BatchId);

        var updatedHourlyEntry = database.GetTimeEntryById(hourlyEntry.Id);
        var updatedRvgEntry = database.GetTimeEntryById(rvgEntry.Id);
        var updatedIgnoredEntry = database.GetTimeEntryById(ignoredEntry.Id);

        Assert.NotNull(updatedHourlyEntry);
        Assert.NotNull(updatedRvgEntry);
        Assert.NotNull(updatedIgnoredEntry);
        Assert.True(updatedHourlyEntry!.Billed);
        Assert.True(updatedRvgEntry!.Billed);
        Assert.Equal(batch.BatchId, updatedHourlyEntry.BillingBatchId);
        Assert.Equal(batch.BatchId, updatedRvgEntry.BillingBatchId);
        Assert.NotNull(updatedHourlyEntry.BilledUtc);
        Assert.NotNull(updatedRvgEntry.BilledUtc);
        Assert.False(updatedIgnoredEntry!.Billed);

        var snapshot = database.GetLatestRvgBillingSnapshot(matterRvg.Id);
        Assert.NotNull(snapshot);
        Assert.Equal("SIG-1", snapshot!.Signature);
        Assert.Equal(250m, snapshot.Total);
        Assert.Equal(batch.BatchId, snapshot.BatchId);

        var finalizedBatch = database.GetBillingBatchById(batch.BatchId);
        Assert.NotNull(finalizedBatch);
        Assert.NotNull(finalizedBatch!.FinalizedUtc);
    }

    [Fact]
    public void FinalizeBatch_WritesBreakdownJsonRoundtrip()
    {
        using var fixture = new BillingFixture();
        var database = fixture.Database;
        var billingService = fixture.Service;

        var matterRvg = database.CreateMatter("444/24");
        matterRvg.BillingType = BillingType.Rvg;
        matterRvg.SubjectValueEur = 500m;
        matterRvg.BusinessFee13Enabled = true;
        database.UpdateMatter(matterRvg);

        var now = DateTime.UtcNow;
        var rvgEntry = database.CreateTimeEntry(matterRvg.Id, now.AddMinutes(-20), "#Test");
        rvgEntry = database.UpdateTimeEntry(rvgEntry.Id, matterRvg.Id, now.AddMinutes(-20), now.AddMinutes(-10), null, null);

        var batch = billingService.CreateBillingBatchDraft(new[] { rvgEntry.Id });
        var cases = database.GetBillingCasesForBatch(batch.BatchId);
        var rvgCase = cases.Single(billingCase => billingCase.MatterId == matterRvg.Id);

        database.UpdateBillingCaseRvgData(
            rvgCase.Id,
            "SIG-2",
            250m,
            false,
            null,
            0m);

        database.UpdateBillingBatchPdfPath(batch.BatchId, "export.pdf");

        var expectedBreakdown = RvgCalculator.CalculateBreakdown(matterRvg, new RvgFeeTableService());

        billingService.FinalizeBatch(batch.BatchId);

        var snapshot = database.GetLatestRvgBillingSnapshot(matterRvg.Id);
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.BreakdownJson));

        var reloadedBreakdown = RvgBreakdownSerializer.Deserialize(snapshot.BreakdownJson);
        Assert.NotNull(reloadedBreakdown);
        Assert.Equal(expectedBreakdown.Total, reloadedBreakdown!.Total);
        Assert.Equal(expectedBreakdown.Items.Count, reloadedBreakdown.Items.Count);
        Assert.Equal(expectedBreakdown.Items[0].Name, reloadedBreakdown.Items[0].Name);
        Assert.Equal(expectedBreakdown.Items[0].Factor, reloadedBreakdown.Items[0].Factor);
        Assert.Equal(expectedBreakdown.Items[0].BaseFee, reloadedBreakdown.Items[0].BaseFee);
        Assert.Equal(expectedBreakdown.Items[0].Amount, reloadedBreakdown.Items[0].Amount);
    }

    private sealed class BillingFixture : IDisposable
    {
        public BillingFixture()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"aktetimer-tests-{Guid.NewGuid():N}.db");
            Database = new DatabaseService(dbPath);
            Database.Initialize();
            Service = new BillingService(Database);
        }

        public DatabaseService Database { get; }
        public BillingService Service { get; }

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
