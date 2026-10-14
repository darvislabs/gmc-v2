using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Prototype §9: Final Quote — commission schedule, discount history, quote versions,
/// pricing summary and the UW decision in one view. Read-only composition over existing data.</summary>
public static class FinalQuoteApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{id:long}/final-quote", async (long id, IGmcRepository repo) =>
        {
            var c = await repo.GetCaseAsync(id);
            if (c is null) return Results.NotFound(new { error = "case not found" });
            var quotes = await repo.GetQuoteVersionsAsync(id);
            var current = quotes.FirstOrDefault(q => q.Status == "Current");
            var discounts = await repo.GetDiscountRequestsAsync(id);
            var commission = await repo.GetCommissionScheduleAsync();
            var artifacts = current is null
                ? new List<QuoteArtifactInfo>()
                : await repo.GetQuoteArtifactsAsync(current.QuoteVersionId);
            return Results.Json(new
            {
                caseNumber = c.CaseNumber,
                clientName = c.ClientName,
                status = c.Status,
                quotes = quotes.Select(q => new
                {
                    q.QuoteVersionId, q.VersionNo, q.Status, q.CreatedBy, q.CreatedAtUtc,
                    technicalPremium = q.Pricing?.TechnicalPremium,
                    grossPremium = q.GrossPremium,
                    netPremium = q.NetPremium,
                    benefitDeltaPerLife = q.Pricing?.BenefitDeltaPerLife,
                }),
                current = current is null ? null : new
                {
                    current.QuoteVersionId, current.VersionNo,
                    technicalPremium = current.Pricing?.TechnicalPremium,
                    grossPremium = current.GrossPremium,
                    netPremium = current.NetPremium,
                },
                commission = commission,
                discounts,
                artifacts = artifacts.Select(a => new { a.QuoteArtifactId, a.ArtifactType, a.FileName, a.GeneratedBy, a.GeneratedAtUtc }),
            });
        });
    }
}
