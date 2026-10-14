using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Prototype §8: expiring vs proposed benefit delta + competitor summary.
/// Benefit delta lines ride on the current quote (BenefitDeltaJson); competitor quotes from uw.CompetitorQuote.</summary>
public static class BenefitDeltaApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{id:long}/benefits", async (long id, IGmcRepository repo) =>
        {
            var quotes = await repo.GetQuoteVersionsAsync(id);
            var q = quotes.FirstOrDefault(x => x.Status == "Current") ?? quotes.LastOrDefault();
            if (q is null)
                return Results.Json(new { computed = false, message = "No quote on this case yet — run the pipeline first." });
            if (q.BenefitDelta.Count == 0)
                return Results.Json(new { computed = false, message = "Benefit delta not computed — map the expiring + proposed benefit schedules (Mapping Workbench) and re-run." });
            var competitors = await repo.GetCompetitorQuotesForCaseAsync(id);
            var summary = new
            {
                totalLines = q.BenefitDelta.Count,
                improved = q.BenefitDelta.Count(l => l.DeltaPerLife > 0),
                reduced = q.BenefitDelta.Count(l => l.DeltaPerLife < 0),
                newLines = q.BenefitDelta.Count(l => l.Method == "NotApplicable"),
                netImpactPerLife = q.BenefitDelta.Sum(l => l.DeltaPerLife),
            };
            return Results.Json(new
            {
                computed = true,
                summary,
                lines = q.BenefitDelta.Select(l => new
                {
                    benefitName = l.BenefitName,
                    expiringText = l.ExpiringValue?.ToString("N0"),
                    proposedText = l.ProposedValue?.ToString("N0"),
                    changeType = l.DeltaPerLife > 0 ? "Improved" : l.DeltaPerLife < 0 ? "Reduced" : "Unchanged",
                    impactPerLife = l.DeltaPerLife,
                    method = l.Method,
                    rationale = l.Rationale,
                }),
                competitors = competitors.Select(c => new { name = c.Name, sumInsured = c.SumInsured, premium = c.Premium, deltaPct = c.DeltaPct }),
            });
        });
    }
}
