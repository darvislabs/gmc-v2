using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Admin formula governance (prototype §Admin: Medical Trend & IBNR Formula).
/// Persists engine-wide assumptions consumed by the pricing bridge on the next run.
/// Table: uw.FormulaConfig (Key/Value rows) — created on demand.</summary>
public static class FormulaConfigApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/admin/formulas", async (IGmcRepository repo) =>
            Results.Json(await repo.GetFormulaConfigAsync()));

        app.MapPost("/api/v1/admin/formulas", async (Dictionary<string, decimal> dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (!string.Equals(ctx.Session.GetString("role"), "Admin", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(403);
            // accepted keys: TrendPct, IbnrPct (extensible)
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TrendPct", "IbnrPct" };
            foreach (var kv in dto)
                if (!allowed.Contains(kv.Key))
                    return Results.BadRequest(new { error = $"unknown formula key '{kv.Key}' (allowed: TrendPct, IbnrPct)" });
            await repo.SaveFormulaConfigAsync(dto);
            return Results.Json(new { ok = true, saved = dto });
        });
    }
}
