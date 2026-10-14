using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Worker;
using Xunit;

namespace GmcV2.Tests;

/// <summary>
/// INFRW smoke tests — pure, no SQL required (InMemoryRepository + demo chain).
/// Killing mid-way is simulated by re-running RunAsync: the second run must
/// complete without duplicating stage runs (idempotency via InputsHash).
/// </summary>
[Collection("PipelineSerial")] // shares DemoPipelineData statics with the other pipeline suites
public class WorkerSmoke
{
    private static async Task<Case> CreateDemoCaseAsync(IGmcRepository repo)
    {
        var c = await repo.CreateCaseAsync(new Case
        {
            ClientName = "ABC Tech Pvt Ltd",
            BusinessType = Enums.BizRollover,
            BusinessSubType = "Main",
            CreatedBy = "Test",
        });
        foreach (var (docType, name) in new[]
        {
            ("ClaimsRegister", "claims.xlsx"),
            ("Enrollment", "enrollment.xlsx"),
            ("ExpiringPolicy", "expiring.pdf"),
            ("Rfq", "rfq.pdf"),
        })
        {
            await repo.AddDocumentAsync(new CaseDocument
            {
                CaseId = c.CaseId, DocType = docType, FileName = name,
                StorageUri = $"demo://{name}", ParseStatus = "Parsed",
            });
        }
        return c;
    }

    private static async Task<Case> RunSeededAsync(IGmcRepository repo, bool forceGuardrailFail = false)
    {
        DemoPipelineData.Reset();
        DemoPipelineData.ForceGuardrailFail = forceGuardrailFail;
        await SeedRunner.SeedReferenceDataAsync(repo);
        await ProgramSeedDiseases(repo);
        var c = await CreateDemoCaseAsync(repo);
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
        return await orch.RunAsync(c.CaseId);
    }

    /// <summary>Mirror of Program.SeedDiseasesAsync (Program.Main is not directly callable from tests).</summary>
    private static async Task ProgramSeedDiseases(IGmcRepository repo)
    {
        var diseases = new (string Name, string Group, string Icd, bool Chronic)[]
        {
            ("Type 2 Diabetes Mellitus", "Endocrine", "E11.9", true),
            ("Essential (Primary) Hypertension", "Cardiovascular", "I10", true),
            ("Atherosclerotic Heart Disease", "Cardiovascular", "I25.1", true),
            ("Normal Delivery", "Maternity", "O80", false),
            ("Caesarean Delivery", "Maternity", "O82", false),
            ("Acute Appendicitis", "Gastro", "K35.8", false),
            ("Senile Cataract", "Ophthalmic", "H25.9", false),
            ("Calculus of Kidney", "KidneyUrology", "N20.0", false),
            ("Pneumonia", "Respiratory", "J18.9", false),
        };
        foreach (var d in diseases)
            await repo.AddDiseaseAsync(new DiseaseEntry
            {
                DiseaseName = d.Name, DiseaseGroup = d.Group, IcdLevel1 = d.Icd,
                IsChronic = d.Chronic,
                MatchKeywords = new[] { d.Name.Split(' ')[0].ToLowerInvariant() },
            });
    }

    [Fact]
    public async Task Demo_pipeline_produces_current_quote_and_autoquotes()
    {
        var repo = new InMemoryRepository();
        var c = await RunSeededAsync(repo);

        Assert.Equal(Enums.CaseAutoQuoted, c.Status);

        var quote = await repo.GetCurrentQuoteVersionAsync(c.CaseId);
        Assert.NotNull(quote);
        Assert.Equal(Enums.QuoteCurrent, quote!.Status);
        Assert.Equal(1, quote.VersionNo);
        Assert.NotNull(quote.Pricing);
        Assert.True(quote.Pricing!.TechnicalPremium > 0, "technical premium must be positive");
        Assert.NotNull(quote.Guardrails);
        Assert.Equal("AutoQuote", quote.Guardrails!.Outcome);
        // seeded data passes every rule
        Assert.Equal(0, quote.Guardrails.RulesFailed);
        Assert.Equal(0, quote.Guardrails.HardStopCount);
        Assert.InRange(quote.Guardrails.Confidence, 80m, 100m);

        // dataset shape pinned by the brief
        Assert.Equal(1240, quote.Analytics!.CoveredLives);
        Assert.Equal(4180, quote.Analytics.ClaimCount);
        Assert.InRange(quote.Analytics.ChronicPrevalencePct, 18m, 26m);   // ≈21%
        Assert.InRange(quote.Analytics.LargeClaimSharePct, 15m, 22m);     // ≈18%, cap 35
        Assert.InRange(quote.Analytics.AvgAge, 35m, 48m);                 // cap 48
        Assert.True(quote.Analytics.VolatilityCv <= 2.5m, "volatility must pass");
        Assert.True(quote.Analytics.Top5ClaimSharePct < 45m);
        Assert.True(quote.Analytics.SingleLargestSharePct < 20m);
        Assert.True(quote.Analytics.MaternitySharePct < 30m);
        Assert.True(quote.Analytics.ParentSharePct < 20m);
        Assert.True(quote.Analytics.NonNetworkSharePct < 25m);
        Assert.True(quote.Analytics.SeniorAgeSharePct < 30m);

        // all 13 stages present and terminal (re-fetch the stored aggregate —
        // RunAsync's returned object was loaded before the stage runs were written)
        var storedCase = await repo.GetCaseAsync(c.CaseId);
        Assert.NotNull(storedCase);
        var stages = storedCase!.StageRuns.GroupBy(r => r.Stage).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(13, stages.Count);
        Assert.All(Enums.Stages, s => Assert.True(stages.ContainsKey(s), $"stage {s} missing"));
        // CompareCompetition is skipped (no CompetitorQuote doc)
        var cc = storedCase.StageRuns.First(r => r.Stage == "CompareCompetition");
        Assert.Equal(Enums.StageSkipped, cc.State);
        // every other stage completed once
        Assert.All(storedCase.StageRuns.Where(r => r.Stage != "CompareCompetition"), r =>
        {
            Assert.Equal(Enums.StageCompleted, r.State);
            Assert.Equal(1, r.AttemptNo);
        });
    }

    [Fact]
    public async Task Rerun_is_idempotent_no_duplicate_stage_runs()
    {
        var repo = new InMemoryRepository();
        var c = await RunSeededAsync(repo);
        var storedFirst = await repo.GetCaseAsync(c.CaseId);
        Assert.NotNull(storedFirst);
        var runsAfterFirst = storedFirst!.StageRuns.Count;
        Assert.True(runsAfterFirst >= 13, "first run must write the 13 stage runs");

        // "kill" and re-run: second RunAsync must skip every completed stage (same inputs hash)
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
        var c2 = await orch.RunAsync(c.CaseId);
        Assert.Equal(Enums.CaseAutoQuoted, c2.Status);

        var stored = await repo.GetCaseAsync(c.CaseId);
        Assert.NotNull(stored);
        Assert.Equal(runsAfterFirst, stored!.StageRuns.Count); // no duplicates
        Assert.All(stored.StageRuns, r => Assert.Equal(1, r.AttemptNo));
        // idempotency also holds for the quote chain: still exactly one Current version
        var quotes = await repo.GetQuoteVersionsAsync(c.CaseId);
        Assert.Single(quotes);
        Assert.Equal(Enums.QuoteCurrent, quotes[0].Status);
    }

    [Fact]
    public async Task Forced_guardrail_failure_ends_referred_to_uw()
    {
        var repo = new InMemoryRepository();
        var c = await RunSeededAsync(repo, forceGuardrailFail: true);

        var quote = await repo.GetCurrentQuoteVersionAsync(c.CaseId);
        Assert.NotNull(quote);
        Assert.Equal("Referred", quote!.Guardrails!.Outcome);
        Assert.True(quote.Guardrails.RulesFailed > 0);
        // refer does NOT park the pipeline — the quote still publishes
        Assert.Equal(Enums.CaseReferredToUW, c.Status);
        // and the pipeline ran to the end (re-fetch the stored aggregate)
        var storedCase = await repo.GetCaseAsync(c.CaseId);
        Assert.NotNull(storedCase);
        Assert.Equal(13, storedCase!.StageRuns.Select(r => r.Stage).Distinct().Count());
    }

    [Fact]
    public async Task Unreadable_document_blocks_then_resume_completes()
    {
        var repo = new InMemoryRepository();
        DemoPipelineData.Reset();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await ProgramSeedDiseases(repo);

        var c = await CreateDemoCaseAsync(repo);
        // corrupt one document
        var doc = c.Documents.First(d => d.DocType == "ClaimsRegister");
        doc.ParseStatus = "Unreadable";
        await repo.UpdateDocumentAsync(doc);

        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
        var parked = await orch.RunAsync(c.CaseId);

        // blocked at Intake: ExceptionPending + blocking exception row + no quote
        Assert.Equal(Enums.CaseExceptionPending, parked.Status);
        var open = await repo.GetOpenExceptionsAsync(c.CaseId, blockingOnly: true);
        Assert.NotEmpty(open);
        Assert.Contains(open, e => e.Code == Enums.ExcDocUnreadable && e.Blocking);
        Assert.Null(await repo.GetCurrentQuoteVersionAsync(c.CaseId));
        // pipeline did NOT advance past Intake
        Assert.Equal("Intake", parked.CurrentStage);

        // "resolve" the exception by fixing the document, then resume
        var exc = open.First(e => e.Code == Enums.ExcDocUnreadable);
        doc.ParseStatus = "Parsed";
        await repo.UpdateDocumentAsync(doc);
        await repo.ResolveExceptionAsync(exc.ExceptionId, "Test");

        var resumed = await orch.ResumeAsync(c.CaseId);
        Assert.Equal(Enums.CaseAutoQuoted, resumed.Status);
        Assert.NotNull(await repo.GetCurrentQuoteVersionAsync(c.CaseId));
    }

    [Fact]
    public async Task Missing_required_documents_park_awaiting_documents()
    {
        var repo = new InMemoryRepository();
        DemoPipelineData.Reset();
        await SeedRunner.SeedReferenceDataAsync(repo);

        var c = await repo.CreateCaseAsync(new Case
        {
            ClientName = "Incomplete Co", BusinessType = Enums.BizRollover, CreatedBy = "Test",
        });
        await repo.AddDocumentAsync(new CaseDocument
        {
            CaseId = c.CaseId, DocType = "Enrollment", FileName = "e.xlsx",
            StorageUri = "demo://e", ParseStatus = "Parsed",
        });

        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
        var result = await orch.RunAsync(c.CaseId);

        Assert.Equal(Enums.CaseAwaitingDocuments, result.Status);
        Assert.Empty(result.StageRuns); // pipeline never started
        Assert.False(PipelineOrchestrator.ShouldAutoStart(result));
    }

    [Fact]
    public void ShouldAutoStart_requires_all_required_docs_parsed()
    {
        var c = new Case { BusinessType = Enums.BizFresh };
        Assert.False(PipelineOrchestrator.ShouldAutoStart(c)); // nothing uploaded

        c.Documents.Add(new CaseDocument { DocType = "Enrollment", ParseStatus = "Parsed" });
        Assert.False(PipelineOrchestrator.ShouldAutoStart(c)); // Rfq missing

        c.Documents.Add(new CaseDocument { DocType = "Rfq", ParseStatus = "Pending" });
        Assert.False(PipelineOrchestrator.ShouldAutoStart(c)); // not parsed yet

        c.Documents[1].ParseStatus = "Parsed";
        Assert.True(PipelineOrchestrator.ShouldAutoStart(c));
    }

    [Fact]
    public async Task SeedRunner_is_idempotent()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        var master1 = await repo.GetActiveMasterVersionAsync();
        var rules1 = await repo.GetGuardrailRulesAsync();
        await SeedRunner.SeedReferenceDataAsync(repo);
        var master2 = await repo.GetActiveMasterVersionAsync();
        var rules2 = await repo.GetGuardrailRulesAsync();

        Assert.Equal(master1.MasterVersionId, master2.MasterVersionId);
        Assert.Equal(rules1.Count, rules2.Count);
        Assert.Equal(15, rules2.Count);
        Assert.Equal(17, (await repo.GetPaidStatusMapAsync()).Count);
    }
}
