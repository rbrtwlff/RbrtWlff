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
