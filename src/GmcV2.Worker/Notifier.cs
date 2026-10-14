using GmcV2.Core;
using GmcV2.Infrastructure;

namespace GmcV2.Worker;

/// <summary>
/// Fan-out per the workflow §12 matrix: in-app (ops.Notification) always, email when a
/// transport is configured (GMCV2_SMTP_HOST). Never throws — notifications must not
/// break the pipeline.
/// </summary>
public static class Notifier
{
    private static readonly IEmailTransport Email = EmailTransportFactory.FromEnv();

    /// <summary>Role → address map for email fan-out (demo seeds these users). Unknown roles are skipped.</summary>
    private static readonly Dictionary<string, string> RoleEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SalesPerson"] = "sales@opexminds.com",
        ["Underwriter"] = "uw@opexminds.com",
        ["SeniorUnderwriter"] = "senior@opexminds.com",
        ["VP"] = "vp@opexminds.com",
        ["Admin"] = "admin@opexminds.com",
    };

    private static async Task Safe(IGmcRepository repo, Notification n)
    {
        try
        {
            await repo.AddNotificationAsync(n);
            if (Email.IsConfigured && RoleEmails.TryGetValue(n.TargetRole, out var to))
                await Email.SendAsync(to, $"GMC · {n.Title}", $"{n.Body}\n\n— GMC v2, Opexminds");
        }
        catch { /* notification must never break the pipeline */ }
    }
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
}
