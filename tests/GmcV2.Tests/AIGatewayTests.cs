using System.Text.Json;
using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Worker;
using Xunit;

namespace GmcV2.Tests;

/// <summary>
/// AI gateway wiring: env factory, registry semantics, and the CodeClinical AI gap-fill
/// hook (AD-1/AD-2). Pipeline-touching tests build isolated synthetic cases via the
/// RealDataBridge pattern (registered under the REAL case id) — the ABC Tech demo
/// dataset must never leak in. Runs in the PipelineSerial collection alongside the
/// other DemoPipelineData consumers (shared static state, parallel classes race).
/// </summary>
[CollectionDefinition("PipelineSerial")]
public class PipelineSerialCollection { }

[Collection("PipelineSerial")]
public class AIGatewayTests
{
    [Fact]
    public void FromEnv_returns_null_when_provider_unset()
    {
        var provider = Environment.GetEnvironmentVariable("GMCV2_AI_PROVIDER");
        try
        {
            Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", null);
            Assert.Null(OllamaAIProvider.FromEnv());
        }
        finally { Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", provider); }
    }

    [Fact]
    public void FromEnv_returns_null_when_provider_not_ollama()
    {
        var provider = Environment.GetEnvironmentVariable("GMCV2_AI_PROVIDER");
        try
        {
            Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", "openai");
            Assert.Null(OllamaAIProvider.FromEnv());
        }
        finally { Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", provider); }
    }

    [Fact]
    public void FromEnv_returns_provider_without_key_but_IsAvailable_false()
    {
        var provider = Environment.GetEnvironmentVariable("GMCV2_AI_PROVIDER");
        var key = Environment.GetEnvironmentVariable("GMCV2_AI_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", "ollama");
            Environment.SetEnvironmentVariable("GMCV2_AI_KEY", null);
            var p = OllamaAIProvider.FromEnv();
            Assert.NotNull(p);                       // configured
            Assert.Equal("ollama", p!.Name);
            Assert.False(p.IsAvailable);             // key empty ⇒ NOT available (spec: configured AND key non-empty)
        }
        finally
        {
            Environment.SetEnvironmentVariable("GMCV2_AI_PROVIDER", provider);
            Environment.SetEnvironmentVariable("GMCV2_AI_KEY", key);
        }
    }

    [Fact]
    public void NoopAIProvider_satisfies_all_four_interfaces()
    {
        IAIProvider p = new NoopAIProvider();
        Assert.IsAssignableFrom<IScheduleExtractionAgent>(p);
        Assert.IsAssignableFrom<IColumnMappingAgent>(p);
        Assert.IsAssignableFrom<IClinicalCodingAgent>(p);
        Assert.IsAssignableFrom<INarrativeAgent>(p);
        Assert.False(p.IsAvailable);
    }

    [Fact]
    public void Registry_any_available_false_with_no_providers_true_with_fake()
    {
        var empty = new AIProviderRegistry();
        Assert.False(empty.AnyAvailable);

        var fake = new FakeClinicalAgent("J18.9", 0.90m);
        var reg = new AIProviderRegistry { Clinical = fake };
        Assert.True(reg.AnyAvailable);
        Assert.Same(fake, reg.Clinical);
        Assert.Null(reg.Extraction);
    }

    [Fact]
    public async Task Clinical_ai_codes_an_otherwise_unmapped_row()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await Worker.WorkerCli.SeedDemoReferenceData(repo);

        // RealDataBridge-style synthetic upload: exactly ONE claim row with a diagnosis
        // the deterministic tier cannot match. Registered under the REAL case id.
        var c = await SeedCaseAsync(repo, "AI Coded");
        DemoPipelineData.Reset();
        DemoPipelineData.SetCaseData(c.CaseId, OneUnmappedClaim());

        var reg = new AIProviderRegistry { Clinical = new FakeClinicalAgent("J18.9", 0.90m) };
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo(reg), reg);
        await orch.RunAsync(c.CaseId);

        var run = (await repo.GetCaseAsync(c.CaseId))!.StageRuns.First(r => r.Stage == "CodeClinical");
        using var doc = JsonDocument.Parse(run.DetailJson!);
        Assert.Equal(1, doc.RootElement.GetProperty("coded").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("unmapped").GetInt32()); // AI filled the gap
    }

    [Fact]
    public async Task Clinical_low_confidence_counts_unmapped()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await Worker.WorkerCli.SeedDemoReferenceData(repo);

        var c = await SeedCaseAsync(repo, "AI LowConf");
        DemoPipelineData.Reset();
        DemoPipelineData.SetCaseData(c.CaseId, OneUnmappedClaim());

        var reg = new AIProviderRegistry { Clinical = new FakeClinicalAgent("J18.9", 0.50m) }; // < 0.75 band
        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo(reg), reg);
        await orch.RunAsync(c.CaseId);

        var run = (await repo.GetCaseAsync(c.CaseId))!.StageRuns.First(r => r.Stage == "CodeClinical");
        using var doc = JsonDocument.Parse(run.DetailJson!);
        Assert.Equal(1, doc.RootElement.GetProperty("unmapped").GetInt32()); // not safely coded
    }

    [Fact]
    public async Task Clinical_unavailable_falls_back_to_unmapped()
    {
        var repo = new InMemoryRepository();
        await SeedRunner.SeedReferenceDataAsync(repo);
        await Worker.WorkerCli.SeedDemoReferenceData(repo);

        var c = await SeedCaseAsync(repo, "AI Offline");
        DemoPipelineData.Reset();
        DemoPipelineData.SetCaseData(c.CaseId, OneUnmappedClaim());

        var orch = new PipelineOrchestrator(repo, StageHandlers.Demo()); // no registry at all
        await orch.RunAsync(c.CaseId);

        var run = (await repo.GetCaseAsync(c.CaseId))!.StageRuns.First(r => r.Stage == "CodeClinical");
        using var doc = JsonDocument.Parse(run.DetailJson!);
        Assert.Equal(1, doc.RootElement.GetProperty("unmapped").GetInt32()); // exact deterministic fallback
    }

    private static StageHandlers.DemoData OneUnmappedClaim()
    {
        var data = new StageHandlers.DemoData();
        data.Members.Add(("EMP00001", "Employee", "Male", 40, false, false));
        data.Claims.Add(("CL000001", "EMP00001", "Paid", 10_000m, false, false, false, "Unheard-Of Syndrome X"));
        return data;
    }

    private static async Task<Case> SeedCaseAsync(InMemoryRepository repo, string name)
    {
        var c = await repo.CreateCaseAsync(new Case { ClientName = name, BusinessType = Enums.BizRollover, CreatedBy = "test" });
        foreach (var t in new[] { "ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq" })
            await repo.AddDocumentAsync(new CaseDocument { CaseId = c.CaseId, DocType = t, FileName = t + ".csv", ParseStatus = "Parsed", StorageUri = "test://" });
        return c;
    }

    /// <summary>Fake clinical agent: always codes to Pneumonia (J18.9) with the given confidence.</summary>
    private sealed class FakeClinicalAgent(string code, decimal conf) : IClinicalCodingAgent
    {
        public string Name => "fake-clinical";
        public bool IsAvailable => true;
        public Task<(string? Icd10Code, string? ProcedureCode, decimal Confidence, string? Rationale)> CodeAsync(
            string diseaseText, IReadOnlyList<(string Icd10, string Description)> diseaseMaster)
            => Task.FromResult(((string?)code, (string?)null, conf, (string?)"fake: unit-test coding"));
    }
}
