using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Multi-year company experience API (prototype §5 3-Year Company Experience panel).
/// Returns policy-year rows: exposure lives, paid claims, frequency, burn cost per life.</summary>
public static class MultiYearApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{caseId}/multiyear", async (long caseId, IGmcRepository repo) =>
        {
            var claims = await repo.GetStdClaimsAsync(caseId);
            var lives = await repo.GetStdMemberCountAsync(caseId);
            var rows = Infrastructure.MultiYearEngine.Compute(claims, lives);
            return Results.Json(new
            {
                caseId,
                years = rows,
                totalPaid = rows.Sum(r => r.PaidAmount),
                latestYear = rows.LastOrDefault(r => r.PolicyYear != "Unknown")?.PolicyYear,
            });
        });
    }
}
