using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Worker;
using Xunit;

namespace GmcV2.Tests;

/// <summary>
/// Mapping Workbench core semantics (workflow §6.2): retarget pins the column with
/// TierHumanConfirmed and an append-only ColumnMapping row, and both survive a force
/// pipeline re-run (Profile re-projection + Map stage must never clobber human
/// decisions). Mirrors ApiMapping's write path against InMemoryRepository — the API
/// layer is a thin pass-through over these repo calls.
/// </summary>
[Collection("PipelineSerial")]
public class MappingWorkbenchTests
{
    [Fact]
    public async Task Retarget_survives_force_pipeline_rerun()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await WorkerCli.SeedDemoReferenceData(repo);
        var c = await SeedCaseAsync(repo, "Map Retarget");
        DemoPipelineData.Reset();
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo(), null);
        await orch.RunAsync(c.CaseId); // profile + map create source columns

        // human retarget: Member ID (enrollment col 0) → HasChronic is taken; pick
        // 'DiagnosisText' claim on the ENROLLMENT 'Chronic Flag' column then re-map back —
        // instead use the canonical workbench flow: retarget 'Age' → DateOfBirth-like
        // clearance is invalid, so exercise the real path: retarget Enrollment 'Gender'
        // column to null (clear) and 'Chronic Flag' to 'Gender'.
        var enrollmentDoc = c.Documents.First(d => d.DocType == "Enrollment");
        var cols = await repo.GetSourceColumnsAsync(enrollmentDoc.DocumentId);
        var genderCol = cols.Single(x => x.Header == "Gender");
        var chronicCol = cols.Single(x => x.Header == "Chronic Flag");

        await ApplyHumanDecision(repo, genderCol.SourceColumnId, target: null, user: "uw@test");
        await ApplyHumanDecision(repo, chronicCol.SourceColumnId, target: "Gender", user: "uw@test");

        var after = await repo.GetSourceColumnsAsync(enrollmentDoc.DocumentId);
        Assert.Null(after.Single(x => x.SourceColumnId == genderCol.SourceColumnId).TargetParameter);
        var chronic = after.Single(x => x.SourceColumnId == chronicCol.SourceColumnId);
        Assert.Equal("Gender", chronic.TargetParameter);
        Assert.Equal(Enums.TierHumanConfirmed, chronic.ProvenanceTier);
        Assert.Equal(1.00m, chronic.Confidence);

        // force re-run: human decisions must survive Profile re-projection + Map stage
        await orch.RunAsync(c.CaseId, force: true);

        var afterRerun = await repo.GetSourceColumnsAsync(enrollmentDoc.DocumentId);
        Assert.Null(afterRerun.Single(x => x.SourceColumnId == genderCol.SourceColumnId).TargetParameter);
        var chronicAfter = afterRerun.Single(x => x.SourceColumnId == chronicCol.SourceColumnId);
        Assert.Equal("Gender", chronicAfter.TargetParameter);
        Assert.Equal(Enums.TierHumanConfirmed, chronicAfter.ProvenanceTier);

        // append-only audit: retargets recorded as versioned mapping rows
        var latest = await repo.GetLatestMappingAsync(chronicCol.SourceColumnId);
        Assert.NotNull(latest);
        Assert.Equal("Gender", latest!.TargetParameter);
        Assert.Equal(Enums.TierHumanConfirmed, latest.ProvenanceTier);
    }

    [Fact]
    public async Task Conflict_then_resolve_awards_winner_and_clears_loser()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await WorkerCli.SeedDemoReferenceData(repo);
        var c = await SeedCaseAsync(repo, "Map Conflict");
        DemoPipelineData.Reset();
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo(), null);
        await orch.RunAsync(c.CaseId);

        var claimsDoc = c.Documents.First(d => d.DocType == "ClaimsRegister");
        var cols = await repo.GetSourceColumnsAsync(claimsDoc.DocumentId);
        var diagnosisCol = cols.Single(x => x.Header == "Diagnosis");
        var statusCol = cols.Single(x => x.Header == "Claim Status");

        // both columns claim 'ClaimStatus' → conflict state (two active claimants)
        await ApplyHumanDecision(repo, diagnosisCol.SourceColumnId, "ClaimStatus", "uw@test");
        var dup = (await repo.GetSourceColumnsAsync(claimsDoc.DocumentId))
            .Count(x => x.TargetParameter == "ClaimStatus");
        Assert.Equal(2, dup); // ApiMapping would 409 here; workbench shows the conflict panel

        // resolve: status column keeps it, diagnosis column is cleared
        await ApplyHumanDecision(repo, diagnosisCol.SourceColumnId, null, "uw@test");
        var after = await repo.GetSourceColumnsAsync(claimsDoc.DocumentId);
        Assert.Equal("ClaimStatus", after.Single(x => x.SourceColumnId == statusCol.SourceColumnId).TargetParameter);
        Assert.Null(after.Single(x => x.SourceColumnId == diagnosisCol.SourceColumnId).TargetParameter);
        Assert.Equal(Enums.TierHumanConfirmed, after.Single(x => x.SourceColumnId == diagnosisCol.SourceColumnId).ProvenanceTier);
    }

    /// <summary>Exact write path ApiMapping uses for a human retarget/clear decision.</summary>
    private static async Task ApplyHumanDecision(InMemoryRepository repo, long sourceColumnId, string? target, string user)
    {
        // resolve the column across all case documents (workbench does the same scan)
        var all = new List<SourceColumn>();
        foreach (var caseDoc in (await Task.FromResult(repo)).ListCasesAsync().GetAwaiter().GetResult())
            foreach (var d in caseDoc.Documents.Where(d => d.ParseStatus == "Parsed"))
                all.AddRange(await repo.GetSourceColumnsAsync(d.DocumentId));
        var col = all.Single(x => x.SourceColumnId == sourceColumnId);
        col.TargetParameter = target;
        col.Confidence = 1.00m;
        col.ProvenanceTier = Enums.TierHumanConfirmed;
        col.Reason = target is null ? "Human cleared mapping in Mapping Workbench" : $"Human retarget in Mapping Workbench → {target}";
        col.IsRequired = target is not null && MappingParameters.IsRequired(target);
        await repo.AddOrUpdateSourceColumnAsync(col);
        await repo.AddColumnMappingAsync(new ColumnMapping
        {
            SourceColumnId = col.SourceColumnId,
            TargetParameter = target ?? "Unmapped",
            Confidence = 1.00m,
            ProvenanceTier = Enums.TierHumanConfirmed,
            AutoApproved = true,
            ApprovedBy = user,
        });
    }

    private static async Task<Case> SeedCaseAsync(InMemoryRepository repo, string name)
    {
        var c = await repo.CreateCaseAsync(new Case { ClientName = name, BusinessType = Enums.BizRollover, CreatedBy = "test" });
        foreach (var t in new[] { "ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq" })
            await repo.AddDocumentAsync(new CaseDocument { CaseId = c.CaseId, DocType = t, FileName = t + ".csv", ParseStatus = "Parsed", StorageUri = "test://" });
        return c;
    }
}
