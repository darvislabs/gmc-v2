using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Prototype §5 UW Analytics full suite: persisted snapshot + dimension breakdowns +
/// SI-scenario + trend narrative. One endpoint powers the #/analytics page.</summary>
public static class AnalyticsApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{id:long}/analytics", async (long id, IGmcRepository repo) =>
        {
            var snap = await repo.GetAnalyticsSnapshotAsync(id);
            if (snap is null) return Results.Json(new { computed = false, message = "Run the pipeline to compute analytics." });
            var si = await repo.GetSIScenarioAsync(id);
            return Results.Json(new
            {
                computed = true,
                snapshot = snap,
                siScenario = si,
            });
        });
    }
}
