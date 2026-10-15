using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Unified queue rows for the v3 role-first shell (prototype UW Queue / Sales Queue):
/// per-case lives, latest premium, referral reason and priority — one additive read endpoint.</summary>
public static class QueueApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/queue", async (IGmcRepository repo) =>
        {
            var cases = await repo.ListCasesAsync(null);
            var rows = new List<object>(cases.Count);
            foreach (var c in cases)
            {
                QuoteVersion? quote = null;
                try
                {
                    var quotes = await repo.GetQuoteVersionsAsync(c.CaseId);
                    quote = quotes.FirstOrDefault(q => q.Status == Enums.QuoteCurrent)
                            ?? quotes.OrderByDescending(q => q.VersionNo).FirstOrDefault();
                }
                catch { /* quote read failure → row with nulls */ }

                var gr = quote?.Guardrails;
                var failed = gr?.Evaluations.Where(e => !e.Passed).ToList() ?? new List<GuardrailEvaluation>();
                var reason = failed.Count > 0
                    ? string.Join("; ", failed.Select(e => e.Message))
                    : "Passed all guardrails";
                var priority = failed.Any(e => e.Severity == "HardStop") ? "High"
                    : failed.Count > 0 ? "Medium" : "Low";

                rows.Add(new
                {
                    caseId = c.CaseId,
                    caseNumber = c.CaseNumber,
                    clientName = c.ClientName,
                    businessType = c.BusinessType,
                    status = c.Status,
                    currentStage = c.CurrentStage,
                    lives = quote?.CoveredLives ?? quote?.Analytics?.CoveredLives ?? 0,
                    premium = quote?.GrossPremium,
                    reason,
                    priority,
                    isAutoQuote = c.Status == Enums.CaseAutoQuoted,
                    isReferral = c.Status == Enums.CaseReferredToUW,
                    updatedAt = c.UpdatedAtUtc,
                });
            }
            return Results.Json(rows);
        });
    }
}
