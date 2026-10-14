using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Knowledge;
using GmcV2.Worker;
using Xunit;

namespace GmcV2.Tests;

[Collection("PipelineSerial")] // shares DemoPipelineData statics with the other pipeline suites
public class ArtifactsNotifierTests
{
    private static readonly string[][] DemoDocRows =
    {
        new[] { "Claim No", "Member ID", "Claim Status", "Sanctioned Amt", "Diagnosis" },
        new[] { "Member ID", "Relationship", "Gender", "Age", "Chronic Flag" },
        new[] { "Benefit", "Value" },
        new[] { "Benefit", "Value" },
    };

    private static async Task<Case> SeedCaseAsync(InMemoryRepository repo, string name)
    {
        var c = await repo.CreateCaseAsync(new Case { ClientName = name, BusinessType = Enums.BizRollover, CreatedBy = "test" });
        var types = new[] { "ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq" };
        for (var i = 0; i < types.Length; i++)
            await repo.AddDocumentAsync(new CaseDocument { CaseId = c.CaseId, DocType = types[i], FileName = types[i] + ".csv", ParseStatus = "Parsed", StorageUri = "test://" });
        return c;
    }

    private static (Case C, QuoteVersion Q) MakeQuote(InMemoryRepository repo)
    {
        // mirror run-demo seeding: diseases + demo master (via WorkerCli alias)
        Worker.WorkerCli.SeedDemoReferenceData(repo).GetAwaiter().GetResult();
        var c = SeedCaseAsync(repo, "Artifact Test").GetAwaiter().GetResult();
        DemoPipelineData.Reset();
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
        var c2 = orch.RunAsync(c.CaseId).GetAwaiter().GetResult();
        var q = repo.GetCurrentQuoteVersionAsync(c.CaseId).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("no quote after demo run");
        return (c2, q);
    }

    [Fact]
    public void Generate_JsonCsvHtml_ContainMoneyAndBridge()
    {
        var repo = new InMemoryRepository();
        SeedRunner.SeedReferenceDataAsync(repo).GetAwaiter().GetResult();
        var (c, q) = MakeQuote(repo);

        var (json, csv, html) = QuoteArtifacts.Generate(c, q);
        Assert.Contains("14350000", json.Replace(",", "").Replace(" ", "")) ;
        Assert.Contains("Technical Premium", csv);
        Assert.Contains("GST", csv);
        Assert.Contains("PRICING BRIDGE", html);
        Assert.Contains("Artifact Test", html);
        Assert.Contains("₹14,350,000", html);
    }

    [Fact]
    public async Task PipelineRun_PersistsThreeArtifacts()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        var (c, q) = MakeQuote(repo);

        var artifacts = await repo.GetQuoteArtifactsAsync(q.QuoteVersionId);
        Assert.Equal(3, artifacts.Count);
        Assert.Contains(artifacts, a => a.ArtifactType == "json");
        Assert.Contains(artifacts, a => a.ArtifactType == "csv");
        Assert.Contains(artifacts, a => a.ArtifactType == "html");
        Assert.All(artifacts, a => Assert.True(a.SizeBytes > 200));

        var (type, content) = (await repo.GetQuoteArtifactContentAsync(artifacts[0].QuoteArtifactId)).GetValueOrDefault();
        Assert.False(string.IsNullOrEmpty(content));
    }

    [Fact]
    public async Task Notifier_AutoQuoted_NotifiesSales()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        var (c, q) = MakeQuote(repo);

        // Publish stage fans out automatically (workflow §12) — no manual call needed
        var notifs = await repo.GetNotificationsAsync("SalesPerson", userId: null);
        var n = Assert.Single(notifs, x => x.CaseId == c.CaseId);
        Assert.Equal("CaseAutoQuoted", n.Kind);
        Assert.Contains("14,350,000", n.Body);
        Assert.False(n.IsRead);
    }

    [Fact]
    public async Task Notifier_Referred_NotifiesUW()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        var c = await SeedCaseAsync(repo, "Ref Test");
        var q = new QuoteVersion { CaseId = c.CaseId, VersionNo = 1, Guardrails = new GuardrailResult { Outcome = "Referred", Confidence = 55m } };

        await Notifier.CaseReferred(repo, c, q);
        var notifs = await repo.GetNotificationsAsync("Underwriter", userId: null);
        var n = Assert.Single(notifs, x => x.CaseId == c.CaseId);
        Assert.Equal("CaseReferred", n.Kind);
        Assert.Contains("Referred", n.Body);
    }
}
