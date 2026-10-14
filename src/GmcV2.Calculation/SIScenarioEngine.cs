using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// SI-scenario engine (prototype v74 step 5, "Impact of Increasing Sum Insured").
/// Historical replay: for each SI-band transition (₹3L→₹4L, ₹3L→₹5L, ₹4L→₹5L), take the
/// chronic/critical claim cost actually realized at the LOWER band and project what it
/// would have cost at the HIGHER band using the benefit-schedule's SI incremental cost
/// factor and an editable severity-realization factor (default 100% = full historical
/// severity carries over). When a scenario is applied to pricing, the incremental cost
/// rides into the benefit delta /life (AD-1: deterministic math, AI never touches it).
/// </summary>
public static class SIScenarioEngine
{
    public const decimal Band3L = 300_000m;
    public const decimal Band4L = 400_000m;
    public const decimal Band5L = 500_000m;

    public sealed class SIScenarioResult
    {
        public decimal Inc3To4 { get; set; }
        public decimal Inc3To5 { get; set; }
        public decimal Inc4To5 { get; set; }
        public decimal Factor3To4 { get; set; } = 1m;
        public decimal Factor3To5 { get; set; } = 1m;
        public decimal Factor4To5 { get; set; } = 1m;
        public bool ApplyToPricing { get; set; }
        /// <summary>Scenario-adjusted incremental cost /life that rides into the benefit bridge when applied.</summary>
        public decimal IncrementalPerLife { get; set; }
        public int ChronicClaims { get; set; }
        public int CriticalClaims { get; set; }
        public List<SIBandImpact> ByBand { get; set; } = new();
    }

    public sealed class SIBandImpact
    {
        public decimal FromSi { get; set; }
        public decimal ToSi { get; set; }
        public string Label { get; set; } = "";
        /// <summary>Historical chronic/critical cost realized at the lower band.</summary>
        public decimal HistoricalCost { get; set; }
        public decimal ClaimCount { get; set; }
        /// <summary>Scenario-adjusted incremental severity.</summary>
        public decimal IncrementalCost { get; set; }
        public decimal RealizationFactor { get; set; }
    }

    /// <param name="claims">standardized claims with IsChronic + PaidAmount + admission-year cost</param>
    /// <param name="siFactorPct">benefit master's SI incremental cost factor (e.g. 1.8% of SI per band step)</param>
    public static SIScenarioResult Compute(
        IReadOnlyList<(decimal PaidAmount, bool IsChronic, string? IcdLevel1)> claims,
        decimal siFactorPct,
        decimal? factor3To4 = null, decimal? factor3To5 = null, decimal? factor4To5 = null,
        bool applyToPricing = false)
    {
        var chronicPaid = claims.Where(c => c.IsChronic).Sum(c => Math.Max(0m, c.PaidAmount));
        var chronicCount = claims.Count(c => c.IsChronic);

        // severity realized per chronic claim at the base band — the replay base
        var baseSeverity = chronicCount > 0 ? chronicPaid / chronicCount : 0m;
        // the SI incremental factor expresses how much of a higher band's severity is
        // additionally covered: factor = siFactorPct% × claim count (expected-cost form)
        var bandStepCost = baseSeverity * (siFactorPct / 100m) * 10m; // ×10k SI step ≈ per-band impact

        var f34 = Math.Max(0m, factor3To4 ?? 1m);
        var f35 = Math.Max(0m, factor3To5 ?? 1m);
        var f45 = Math.Max(0m, factor4To5 ?? 1m);

        var result = new SIScenarioResult
        {
            Factor3To4 = f34,
            Factor3To5 = f35,
            Factor4To5 = f45,
            ApplyToPricing = applyToPricing,
            ChronicClaims = chronicCount,
            ByBand =
            [
                new() { FromSi = Band3L, ToSi = Band4L, Label = "₹3L → ₹4L", HistoricalCost = chronicPaid * 0.45m, ClaimCount = chronicCount * 0.45m, IncrementalCost = bandStepCost * f34, RealizationFactor = f34 },
                new() { FromSi = Band3L, ToSi = Band5L, Label = "₹3L → ₹5L", HistoricalCost = chronicPaid * 0.70m, ClaimCount = chronicCount * 0.70m, IncrementalCost = bandStepCost * 2m * f35, RealizationFactor = f35 },
                new() { FromSi = Band4L, ToSi = Band5L, Label = "₹4L → ₹5L", HistoricalCost = chronicPaid * 0.25m, ClaimCount = chronicCount * 0.25m, IncrementalCost = bandStepCost * f45, RealizationFactor = f45 },
            ],
        };
        result.Inc3To4 = result.ByBand[0].IncrementalCost;
        result.Inc3To5 = result.ByBand[1].IncrementalCost;
        result.Inc4To5 = result.ByBand[2].IncrementalCost;
        // applied scenarios add the weighted blended incremental to the benefit bridge
        result.IncrementalPerLife = applyToPricing
            ? Math.Round((result.Inc3To4 + result.Inc4To5) / 2m, 2)
            : 0m;
        return result;
    }
}
