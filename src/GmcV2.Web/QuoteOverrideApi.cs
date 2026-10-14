using GmcV2.Core;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>Manual quote override (§11): underwriter supersedes the live quote with an
/// adjusted version — discount, loading, or manual premium. Reason is mandatory and
/// permanently recorded (RevisionReason/RevisionNotes + UnderwritingAction).</summary>
public sealed record QuoteOverrideDto(
    decimal? TechnicalPremium,
    decimal? DiscountPct,
    string Reason,
    string? Notes);

public static class QuoteOverrideApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/cases/{id:long}/quotes/override", async (long id, QuoteOverrideDto dto, IGmcRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return Results.BadRequest(new { error = "reason is mandatory for an override" });

            var c = await repo.GetCaseAsync(id);
            if (c is null) return Results.NotFound(new { error = "case not found" });

            var quotes = await repo.GetQuoteVersionsAsync(id);
            var current = quotes.FirstOrDefault(q => q.Status == Enums.QuoteCurrent);
            if (current?.Pricing is null)
                return Results.BadRequest(new { error = "no current priced quote to override" });

            var tech = dto.TechnicalPremium
                ?? current.Pricing.TechnicalPremium * (1m - (dto.DiscountPct ?? 0m) / 100m);
            if (tech <= 0m)
                return Results.BadRequest(new { error = "technical premium must be positive" });

            var versionNo = quotes.Max(q => q.VersionNo) + 1;
            var next = new QuoteVersion
            {
                CaseId = id,
                VersionNo = versionNo,
                Status = Enums.QuoteCurrent,
                GenerationMode = "Manual",
                SupersedesQuoteVersionId = (int)current.QuoteVersionId,
                RevisionReason = dto.Reason.Trim(),
                RevisionNotes = dto.Notes,
                // GrossPremium = pre-GST grossed-up premium (repo applies GST 18% on persist)
                GrossPremium = tech,
                NetPremium = tech,
                Pricing = CloneWithTechnical(current.Pricing, tech),
                Analytics = current.Analytics,
                Guardrails = current.Guardrails,
                BenefitDelta = current.BenefitDelta,
            };
            // DB-level supersede: all other Current rows → Superseded with the new version id
            await repo.AddQuoteVersionAsync(next);
            await repo.SupersedeQuoteVersionAsync(id, (int)next.QuoteVersionId);

            await repo.AddUnderwritingActionAsync(new UnderwritingAction
            {
                CaseId = id,
                QuoteVersionId = (int)next.QuoteVersionId,
                Action = "Revise",
                Actor = "api",
                Remarks = $"Override V{versionNo}: {dto.Reason.Trim()}",
            });

            Notifier.QuoteSuperseded(repo, c, (int)current.VersionNo, versionNo);

            return Results.Json(new
            {
                quoteVersionId = next.QuoteVersionId,
                versionNo = versionNo,
                technicalPremium = tech,
                grossPremium = next.GrossPremium,
                supersedes = current.QuoteVersionId,
            });
        });
    }

    private static PricingResult CloneWithTechnical(PricingResult src, decimal tech)
    {
        var clone = new PricingResult
        {
            MasterVersionId = src.MasterVersionId,
            MasterVersionRef = src.MasterVersionRef,
            Components = src.Components,
            ObservedBurnPerLife = src.ObservedBurnPerLife,
            NormalizedBurnPerLife = src.NormalizedBurnPerLife,
            UltimateBurnPerLife = src.UltimateBurnPerLife,
            TrendedBurnPerLife = src.TrendedBurnPerLife,
            BenefitDeltaPerLife = src.BenefitDeltaPerLife,
            NetTechnicalPerLife = src.NetTechnicalPerLife,
            TechnicalPremium = tech,
            LoadingsPerLife = src.LoadingsPerLife,
            GrossPremiumPerLife = src.GrossPremiumPerLife,
            InputSnapshotJson = src.InputSnapshotJson,
            InputsHash = src.InputsHash,
            ComputedAtUtc = src.ComputedAtUtc,
        };
        return clone;
    }
}
