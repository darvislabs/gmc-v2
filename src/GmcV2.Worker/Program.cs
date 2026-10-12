using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Worker;

namespace GmcV2.Worker;

/// <summary>
/// Worker console host — also the smoke-test entrypoint.
///   seed      → seed reference data (memory by default; GMCV2_DBMODE=sql → SQL)
///   run-demo  → ABC Tech case end-to-end on InMemoryRepository, prints the quote
///   serve-sql → same demo on SqlRepository with connection string from env GMCV2_SQL
/// Exit code 0 on success.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var cmd = args.FirstOrDefault()?.ToLowerInvariant() ?? "run-demo";
        try
        {
            switch (cmd)
            {
                case "seed":
                    return await SeedAsync();
                case "run-demo":
                    return await RunDemoAsync(useSql: false);
                case "serve-sql":
                    return await RunDemoAsync(useSql: true);
                default:
                    Console.Error.WriteLine($"Unknown command '{cmd}'. Use: seed | run-demo | serve-sql");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Seeds kb disease taxonomy through the repository (works for memory + SQL;
    /// the SQL database already carries the 09_seed.sql rows — AddDiseaseAsync is
    /// idempotent by (name, icd)).
    /// </summary>
    private static readonly (string Name, string Group, string Icd, bool Chronic, string[] Keywords)[] DemoDiseases =
    {
        ("Type 2 Diabetes Mellitus", "Endocrine", "E11.9", true, new[] { "diabetes", "sugar", "t2dm", "dm" }),
        ("Essential (Primary) Hypertension", "Cardiovascular", "I10", true, new[] { "hypertension", "htn", "bp" }),
        ("Atherosclerotic Heart Disease", "Cardiovascular", "I25.1", true, new[] { "cad", "coronary", "heart disease", "angioplasty", "stent" }),
        ("Normal Delivery", "Maternity", "O80", false, new[] { "normal delivery", "vaginal delivery", "normal" }),
        ("Caesarean Delivery", "Maternity", "O82", false, new[] { "caesarean", "cesarean", "c-section", "lscs" }),
        ("Acute Appendicitis", "Gastro", "K35.8", false, new[] { "appendicitis", "appendix" }),
        ("Senile Cataract", "Ophthalmic", "H25.9", false, new[] { "cataract" }),
        ("Calculus of Kidney", "KidneyUrology", "N20.0", false, new[] { "renal calculus", "kidney stone", "ureteric", "calculus" }),
        ("Pneumonia", "Respiratory", "J18.9", false, new[] { "pneumonia" }),
    };

    private static async Task SeedDiseasesAsync(IGmcRepository repo)
    {
        foreach (var d in DemoDiseases)
            await repo.AddDiseaseAsync(new DiseaseEntry
            {
                DiseaseName = d.Name,
                DiseaseGroup = d.Group,
                IcdLevel1 = d.Icd,
                IsChronic = d.Chronic,
                MatchKeywords = d.Keywords,
            });
    }

    private static IGmcRepository BuildRepo(bool useSql)
    {
        if (useSql)
        {
            var cs = Environment.GetEnvironmentVariable("GMCV2_SQL")
                ?? "Server=localhost,1433;Database=GMCv2;User Id=sa;Password=Gmc0pex!2026;TrustServerCertificate=True";
            return new SqlRepository(cs);
        }
        return new InMemoryRepository();
    }

    private static async Task<int> SeedAsync()
    {
        var useSql = (Environment.GetEnvironmentVariable("GMCV2_DBMODE") ?? "memory").Equals("sql", StringComparison.OrdinalIgnoreCase);
        var repo = BuildRepo(useSql);
        await SeedRunner.SeedReferenceDataAsync(repo);
        var master = await repo.GetActiveMasterVersionAsync();
        var rules = await repo.GetGuardrailRulesAsync();
        var paid = await repo.GetPaidStatusMapAsync();
        Console.WriteLine($"Seeded ({(useSql ? "sql" : "memory")}): master {master.VersionRef} (id {master.MasterVersionId}), {rules.Count} guardrail rules, {paid.Count} paid-status pairs.");
        return 0;
    }

    private static async Task<int> RunDemoAsync(bool useSql)
    {
        DemoPipelineData.Reset();
        var repo = BuildRepo(useSql);
        await SeedRunner.SeedReferenceDataAsync(repo);
        await SeedDiseasesAsync(repo);

        // ── the ABC Tech case (workflow §13) ──
        var c = new Case
        {
            ClientName = "ABC Tech Pvt Ltd",
            BusinessType = Enums.BizRollover,
            BusinessSubType = "Main",
            CreatedBy = "Sales.Demo",
        };
        c = await repo.CreateCaseAsync(c);

        foreach (var (docType, name) in new[]
        {
            ("ClaimsRegister", "abc-tech-claims-4180.xlsx"),
            ("Enrollment", "abc-tech-enrollment-1280.xlsx"),
            ("ExpiringPolicy", "abc-tech-expiring-policy.pdf"),
            ("Rfq", "abc-tech-proposed-rfq.pdf"),
        })
        {
            await repo.AddDocumentAsync(new CaseDocument
            {
                CaseId = c.CaseId,
                DocType = docType,
                FileName = name,
                StorageUri = $"demo://abc-tech/{name}",
                ParseStatus = "Parsed",
            });
        }

        var orchestrator = new PipelineOrchestrator(repo, StageHandlers.Demo());
        Console.WriteLine($"Case {c.CaseNumber} (id {c.CaseId}) — running 13-stage pipeline ({(useSql ? "SqlRepository" : "InMemoryRepository")}, engines: {(EngineBridge.UsingRealCalculation ? "bound" : "local fallback")})...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        c = await orchestrator.RunAsync(c.CaseId);
        sw.Stop();

        // ── print the quote ──
        var quote = await repo.GetCurrentQuoteVersionAsync(c.CaseId);
        Console.WriteLine();
        Console.WriteLine($"Case number      : {c.CaseNumber}");
        Console.WriteLine($"Case status      : {c.Status}");
        Console.WriteLine($"Elapsed          : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Stage timeline   :");
        foreach (var run in c.StageRuns.OrderBy(r => r.StageRunId))
            Console.WriteLine($"  {run.Stage,-20} attempt {run.AttemptNo}  {run.State,-12} {run.ElapsedSeconds * 1000m,8:0} ms");
        if (quote is null)
        {
            Console.WriteLine("No quote published — case parked with exceptions:");
            foreach (var e in await repo.GetOpenExceptionsAsync(c.CaseId))
                Console.WriteLine($"  {e.Code} (blocking={e.Blocking}): {e.Message}");
            return 1;
        }
        var g = quote.Guardrails;
        var storedCase = await repo.GetCaseAsync(c.CaseId);
        Console.WriteLine();
        Console.WriteLine($"Quote            : V{quote.VersionNo} [{quote.Status}]");
        Console.WriteLine($"Technical premium: ₹{quote.Pricing?.TechnicalPremium ?? quote.GrossPremium:N0}");
        Console.WriteLine($"GST 18%          : ₹{Math.Round((quote.Pricing?.TechnicalPremium ?? quote.GrossPremium) * 0.18m, 2):N0}");
        Console.WriteLine($"Total incl GST   : ₹{Math.Round((quote.Pricing?.TechnicalPremium ?? quote.GrossPremium) * 1.18m, 2):N0}");
        Console.WriteLine($"Guardrail outcome: {g?.Outcome ?? "n/a"} (confidence {g?.Confidence ?? 0})");
        Console.WriteLine($"Covered lives    : {quote.Analytics?.CoveredLives ?? 0}");
        Console.WriteLine($"Burn / life      : ₹{quote.Analytics?.BurnCostPerLife ?? 0:N0}");
        Console.WriteLine($"Benefit delta/life: ₹{quote.Pricing?.BenefitDeltaPerLife ?? 0:N0}");
        Console.WriteLine($"Stage timeline   :");
        foreach (var run in storedCase?.StageRuns.OrderBy(r => r.StageRunId) ?? Enumerable.Empty<CaseStageRun>())
            Console.WriteLine($"  {run.Stage,-20} attempt {run.AttemptNo}  {run.State,-12} {run.ElapsedSeconds * 1000m,8:0} ms");
        if (g is not null && g.RulesFailed > 0)
            foreach (var e in g.Evaluations.Where(e => !e.Passed))
                Console.WriteLine($"  REFER {e.RuleCode}: observed {e.Observed} vs {e.Operator} {e.Threshold}");
        var advisories = (await repo.GetOpenExceptionsAsync(c.CaseId)).Where(e => !e.Blocking).ToList();
        if (advisories.Count > 0)
            foreach (var a in advisories)
                Console.WriteLine($"  advisory {a.Code}: {a.Message}");
        Console.WriteLine();
        return c.Status is Enums.CaseAutoQuoted or Enums.CaseReferredToUW ? 0 : 1;
    }
}
