using GmcV2.Core;
using GmcV2.Infrastructure;

namespace GmcV2.Worker;

public static partial class StageHandlers
{
    /// <summary>Persist the analytics snapshot + per-dimension breakdowns (prototype §5 panels feed
    /// from these rows). Dictionary values are percentages (AnalyticsEngine computes shares).</summary>
    public static async Task PersistAnalyticsAsync(IGmcRepository repo, long caseId, AnalyticsSnapshot snap, long? stageRunId)
    {
        // Deterministic trend narrative — replaced by the NarrativeAgent when AI tier is up (§5 LLM panel)
        snap.TrendNarrative ??=
            $"Book of {snap.CoveredLives} lives, {snap.ClaimCount} claims, incurred {snap.AmountIncurred:N0}. " +
            $"Burn {snap.BurnCostPerLife:N0}/life at {snap.FrequencyPer100:N1} claims per 100 lives. " +
            $"Large claims ({snap.LargeClaimSharePct:N1}% of cost) and chronic prevalence {snap.ChronicPrevalencePct:N1}% " +
            $"drive volatility (CV {snap.VolatilityCv:N2}). Top disease: " +
            (snap.ByDiseaseGroup.OrderByDescending(kv => kv.Value).FirstOrDefault().Key is { Length: > 0 } top ? top : "n/a") + ".";
        var id = await repo.SaveAnalyticsSnapshotAsync(caseId, snap, stageRunId);
        var rows = new List<AnalyticsBreakdownRow>();
        foreach (var (k, v) in snap.ByAgeBand)
            rows.Add(new AnalyticsBreakdownRow { Dimension = "AgeBand", DimensionValue = k, Lives = snap.CoveredLives, ClaimCount = 0, PaidAmount = 0, IncurredAmount = 0, FrequencyPer100 = snap.FrequencyPer100, CostSharePct = v });
        foreach (var (k, v) in snap.ByRelationship)
            rows.Add(new AnalyticsBreakdownRow { Dimension = "Relationship", DimensionValue = k, Lives = snap.CoveredLives, ClaimCount = 0, PaidAmount = 0, IncurredAmount = 0, FrequencyPer100 = snap.FrequencyPer100, CostSharePct = v });
        foreach (var (k, v) in snap.ByDiseaseGroup)
            rows.Add(new AnalyticsBreakdownRow { Dimension = "DiseaseGroup", DimensionValue = k, Lives = 0, ClaimCount = 0, PaidAmount = 0, IncurredAmount = 0, FrequencyPer100 = 0, CostSharePct = v });
        await repo.SaveAnalyticsBreakdownsAsync(id, rows);
    }
}
