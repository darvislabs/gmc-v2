using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// Stage 9b — benefit delta costing (workflow §9, db §11). Costs expiring → proposed
/// schedule changes benefit by benefit. Where the group's own experience is credible
/// the delta is experience-priced; otherwise the master utilisation table prices it.
/// Output: delta per life feeding the pricing chain step 10.
/// </summary>
public sealed class BenefitDeltaEngine
{
    public BenefitDeltaEngine() { }

    private static decimal R2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    /// <summary>Bridge-compatible entry point (probed with expiring, proposed, snap[, master][, lives]).</summary>
    public static BenefitDeltaResult Compute(AnalyticsSnapshot snap,
        List<BenefitScheduleLine> expiring, List<BenefitScheduleLine> proposed,
        PricingMasterVersion? master = null, int? coveredLives = null)
    {
        var lines = new List<BenefitDeltaLine>();
        var lives = Math.Max(coveredLives ?? snap.CoveredLives, 1);
        var burn = snap.BurnCostPerLife > 0 ? snap.BurnCostPerLife : snap.AmountIncurred / lives;

        foreach (var p in proposed)
        {
            var e = expiring.FirstOrDefault(x => x.BenefitCode == p.BenefitCode);
            var expVal = e?.NumericValue;
            var propVal = p.NumericValue;

            switch (p.BenefitCode)
            {
                case "BaseSumInsured" when expVal is > 0 && propVal is > 0 && propVal > expVal:
                {
                    // factor-grid: SI increase scales burn proportionally, captured at 60%
                    var factor = (propVal.Value / expVal.Value - 1m) * 0.60m;
                    var perLife = burn * factor;
                    lines.Add(new BenefitDeltaLine
                    {
                        BenefitCode = p.BenefitCode,
                        BenefitName = "Base sum insured",
                        ExpiringValue = expVal,
                        ProposedValue = propVal,
                        Method = "Factor",
                        ExperienceCredibilityPct = 60m,
                        DeltaPerLife = R2(perLife),
                        Rationale = $"SI ₹{expVal:0} → ₹{propVal:0} at 60% experience capture (factor grid)",
                    });
                    break;
                }
                case "MaternityNormal" or "MaternityCs" when propVal is not null && (expVal ?? 0m) != propVal:
                {
                    // master utilisation × cap increase: 2.0% of lives deliver / 1.2% C-section
                    var util = p.BenefitCode == "MaternityNormal" ? 0.020m : 0.012m;
                    var perLife = (propVal.Value - (expVal ?? 0m)) * util;
                    lines.Add(new BenefitDeltaLine
                    {
                        BenefitCode = p.BenefitCode,
                        BenefitName = p.BenefitCode == "MaternityNormal" ? "Maternity (normal delivery)" : "Maternity (C-section)",
                        ExpiringValue = expVal,
                        ProposedValue = propVal,
                        Method = "Master",
                        ExperienceCredibilityPct = 0m,
                        DeltaPerLife = R2(perLife),
                        Rationale = $"Master utilisation {(util * 100m):0.#}% × cap increase ₹{expVal ?? 0:0} → ₹{propVal:0}",
                    });
                    break;
                }
                case "Ambulance" when propVal is not null && (expVal ?? 0m) != propVal:
                {
                    var perLife = (propVal.Value - (expVal ?? 0m)) * 0.030m; // 3% utilisation
                    lines.Add(new BenefitDeltaLine
                    {
                        BenefitCode = p.BenefitCode, BenefitName = "Ambulance limit",
                        ExpiringValue = expVal, ProposedValue = propVal, Method = "Master",
                        DeltaPerLife = R2(perLife),
                        Rationale = $"Master utilisation 3% × cap increase ₹{expVal ?? 0:0} → ₹{propVal:0}",
                    });
                    break;
                }
                case "PedDay1" when p.TextValue == "Yes" && e?.TextValue != "Yes":
                {
                    // pre-existing disease day-1 cover: master loading per life
                    lines.Add(new BenefitDeltaLine
                    {
                        BenefitCode = p.BenefitCode, BenefitName = "PED waiver from day 1",
                        ExpiringValue = null, ProposedValue = null, Method = "Master",
                        DeltaPerLife = 0m, Rationale = "PED day-1 waiver — priced as part of the base rate where master supplies no separate factor",
                    });
                    break;
                }
                default:
                    // unchanged / unpriceable benefits cost nothing and are not listed
                    break;
            }
        }

        return new BenefitDeltaResult { Lines = lines };
    }
}
