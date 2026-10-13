using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Knowledge;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>
/// Web-side reference-data bootstrap: mirrors the Worker demo seeding (diseases + demo
/// pricing master) so API-driven pipelines have the same clinical master as run-demo.
/// Idempotent: diseases dedupe by (name, icd); master dedupes by VersionRef.
/// </summary>
public static class WebSeed
{
    public static async Task EnsureAsync(IGmcRepository repo)
    {
        // five role users (workflow §3) — PBKDF2-hashed, idempotent by email
        foreach (var u in UserSeed.SeedUsers())
            if (await repo.GetUserByEmailAsync(u.Email) is null)
                await repo.AddUserAsync(u);

        foreach (var d in WorkerCli.DemoDiseases)
            await repo.AddDiseaseAsync(new DiseaseEntry
            {
                DiseaseName = d.Name,
                DiseaseGroup = d.Group,
                IcdLevel1 = d.Icd,
                IsChronic = d.Chronic,
                MatchKeywords = d.Keywords,
            });

        // demo-cohort master (PMV-2026Q4-DEMO) — same params as Worker.EnsureDemoMasterAsync
        var active = await repo.GetActiveMasterVersionAsync();
        if (active.VersionRef.Contains("DEMO")) return;
        var all = await repo.ListMasterVersionsAsync();
        if (all.Any(v => v.VersionRef == "PMV-2026Q4-DEMO"))
        {
            foreach (var v in all.Where(v => v.VersionRef != "PMV-2026Q4-DEMO" && v.Status == "Active"))
            {
                v.Status = "Retired";
                await repo.UpdateMasterVersionAsync(v);
            }
            var existing = all.First(v => v.VersionRef == "PMV-2026Q4-DEMO");
            existing.Status = "Active";
            await repo.UpdateMasterVersionAsync(existing);
            return;
        }
        var demo = new PricingMasterVersion
        {
            VersionRef = "PMV-2026Q4-DEMO",
            Status = "Active",
            EffectiveFromUtc = DateTime.UtcNow,
            MedicalTrendByAgeBand = new[] { 8m, 9m, 10.5m },
            CredibilityZ = 0.60m,
            BenchmarkBurnPerLife = 3_237.42m,
            RiskMarginPct = 11m,
            BaseExpensePct = 9m,
            LargeClaimNormalizationPct = 3m,
            StarRejectionPct = 2m,
            IbnrPctByClaimBand = 3m,
            CommissionPct = 7.5m,
            MaxCommissionPct = 15m,
            MaxDiscountPct = 5m,
            GstPct = 18m,
            SicIncreaseCapturePct = 5.63m,
        };
        await repo.AddMasterVersionAsync(demo);
    }
}
