using GmcV2.Calculation;
using GmcV2.Core;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>
/// SI-impact scenario API (prototype v74 step 5): editable realization factors +
/// apply-to-pricing toggle. GET computes from persisted StdClaims; POST stores the
/// scenario. When applied, the incremental /life rides into the pricing bridge as an
/// override component at recalculation time.
/// </summary>
public static class SIScenarioApi
{
    public sealed record SISaveDto(decimal? Factor3To4, decimal? Factor3To5, decimal? Factor4To5, bool ApplyToPricing);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{id:long}/si-scenario", async (long id, IGmcRepository repo) =>
        {
            var claims = await repo.GetStdClaimsAsync(id);
            if (claims.Count == 0)
                return Results.Json(new { caseId = id, computed = false, message = "No standardized claims — run the pipeline first." });

            var saved = await repo.GetSIScenarioAsync(id);
            var result = SIScenarioEngine.Compute(
                claims.Select(cl => (cl.PaidAmount, cl.IsChronic, cl.DiagnosisText)).ToList(),
                siFactorPct: 1.8m, // benefit master's SI incremental cost factor (§8)
                saved?.Factor3To4, saved?.Factor3To5, saved?.Factor4To5,
                saved?.ApplyToPricing ?? false);

            return Results.Json(new
            {
                caseId = id,
                computed = true,
                chronicClaims = result.ChronicClaims,
                factors = new { f34 = result.Factor3To4, f35 = result.Factor3To5, f45 = result.Factor4To5 },
                applyToPricing = result.ApplyToPricing,
                incrementalPerLife = result.IncrementalPerLife,
                bands = result.ByBand.Select(b => new
                {
                    b.Label, b.FromSi, b.ToSi, b.HistoricalCost, b.ClaimCount, b.IncrementalCost, b.RealizationFactor,
                }),
            });
        });

        app.MapPost("/api/v1/cases/{id:long}/si-scenario", async (long id, SISaveDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            foreach (var f in new[] { dto.Factor3To4, dto.Factor3To5, dto.Factor4To5 })
                if (f is < 0m or > 5m) return Results.BadRequest(new { error = "realization factors must be 0–5 (0%–500% of historical severity)" });

            await repo.SaveSIScenarioAsync(new SIScenarioRow
            {
                CaseId = id,
                Factor3To4 = dto.Factor3To4 ?? 1m,
                Factor3To5 = dto.Factor3To5 ?? 1m,
                Factor4To5 = dto.Factor4To5 ?? 1m,
                ApplyToPricing = dto.ApplyToPricing,
                UpdatedBy = ctx.Session.GetString("email") ?? "api",
            });
            return Results.Json(new { ok = true, applyToPricing = dto.ApplyToPricing, note = dto.ApplyToPricing ? "Applied — re-run the pipeline to see it in the pricing bridge." : "Saved — analysis only." });
        });
    }
}
