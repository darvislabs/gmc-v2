using GmcV2.Core;

namespace GmcV2.Worker;

/// <summary>
/// In-app notification fan-out per the workflow §12 matrix. In-app only (ops.Notification);
/// email is a later pass. Never throws — notifications must not break the pipeline.
/// </summary>
public static class Notifier
{
    public static Task CaseAutoQuoted(IGmcRepository repo, Case c, QuoteVersion q) =>
        Safe(repo, new Notification
        {
            TargetRole = "SalesPerson",
            CaseId = c.CaseId,
            CaseNumber = c.CaseNumber,
            Kind = "CaseAutoQuoted",
            Title = $"Quote ready — {c.ClientName}",
            Body = $"Quote V{q.VersionNo} auto-issued: {q.Pricing?.TechnicalPremium:N0} technical premium. Guardrail confidence {q.Guardrails?.Confidence ?? 0:0.0}.",
            LinkPath = $"/case/{c.CaseId}",
        });

    public static Task CaseReferred(IGmcRepository repo, Case c, QuoteVersion q) =>
        Safe(repo, new Notification
        {
            TargetRole = "Underwriter",
            CaseId = c.CaseId,
            CaseNumber = c.CaseNumber,
            Kind = "CaseReferred",
            Title = $"Case referred — {c.ClientName}",
            Body = $"Quote V{q.VersionNo} needs underwriter review (guardrail: {q.Guardrails?.Outcome}).",
            LinkPath = $"/case/{c.CaseId}",
        });

    public static Task BlockingException(IGmcRepository repo, Case c, CaseException e) =>
        Safe(repo, new Notification
        {
            TargetRole = "Underwriter",
            CaseId = c.CaseId,
            CaseNumber = c.CaseNumber,
            Kind = "ExceptionRaised",
            Title = $"Blocking exception — {c.CaseNumber}",
            Body = $"{e.Code}: {e.Message} Fix at {e.ResolutionTarget}.",
            LinkPath = $"/case/{c.CaseId}",
        });

    public static Task QuoteSuperseded(IGmcRepository repo, Case c, int oldVersion, int newVersion) =>
        Safe(repo, new Notification
        {
            TargetRole = "SalesPerson",
            CaseId = c.CaseId,
            CaseNumber = c.CaseNumber,
            Kind = "QuoteSuperseded",
            Title = $"Quote superseded — {c.ClientName}",
            Body = $"V{oldVersion} superseded by V{newVersion}.",
            LinkPath = $"/case/{c.CaseId}",
        });

    private static async Task Safe(IGmcRepository repo, Notification n)
    {
        try { await repo.AddNotificationAsync(n); }
        catch { /* notification must never break the pipeline */ }
    }
}
