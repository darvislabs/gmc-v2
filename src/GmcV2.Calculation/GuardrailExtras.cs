using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>Guardrail context extensions (prototype §6): benefit-schedule + concentration
/// inputs that live outside the analytics snapshot. Null fields skip their selectors.</summary>
public sealed class GuardrailExtras
{
    /// <summary>Chronic-member share of incurred cost (PED severity proxy). Null when unknown.</summary>
    public decimal? PedCostSharePct { get; init; }
    /// <summary>Largest single disease-group cost share.</summary>
    public decimal? DiseaseGroupConcentrationPct { get; init; }
    /// <summary>(Proposed SI − Expiring SI) / Expiring SI × 100.</summary>
    public decimal? SiIncreasePct { get; init; }
    /// <summary>Base-SI uplift as % of burn/life (benefit-delta cost pressure).</summary>
    public decimal? BenefitDeltaPct { get; init; }
    /// <summary>Proposed schedule lines outside the standard benefit set.</summary>
    public int? NonStandardBenefitCount { get; init; }

    public static GuardrailExtras From(AnalyticsSnapshot snap, List<BenefitScheduleLine> expiring, List<BenefitScheduleLine> proposed)
    {
        var siExp = BaseSi(expiring);
        var siProp = BaseSi(proposed);
        var siInc = siExp > 0 ? (siProp - siExp) / siExp * 100m : (decimal?)null;

        var standard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BaseSumInsured", "MaternityNormal", "MaternityCs", "Ambulance", "Newborn",
            "RoomRent", "Copay", "PedDay1", "PrePostHospitalization", "DayCare",
            "Domiciliary", "OrganDonor", "AYUSH", "AnnualHealthCheck", "Restoration",
            "CorporateBuffer", "DiseaseSubLimit",
        };
        var nonStandard = proposed.Count(l => !standard.Contains(l.BenefitCode ?? ""));

        // Benefit-delta cost pressure (prototype step-5 semantics): each ₹1L band step carries
        // an 18% burn increment (1.8% SI factor × 10 severity multiplier, matching
        // SIScenarioEngine). deltaPct = (steps × 18%) expressed as % of current burn.
        var steps = siExp > 0 ? (siProp - siExp) / 100_000m : 0m;
        var deltaBase = snap.BurnCostPerLife > 0
            ? Math.Abs(steps) * 18m
            : (decimal?)null;

        return new GuardrailExtras
        {
            PedCostSharePct = null, // wired from the member-claim join in Analyze when chronic flags are present
            DiseaseGroupConcentrationPct = snap.ByDiseaseGroup.Count > 0 ? snap.ByDiseaseGroup.Values.Max() : null,
            SiIncreasePct = siInc,
            BenefitDeltaPct = deltaBase,
            NonStandardBenefitCount = nonStandard,
        };
    }

    private static decimal BaseSi(List<BenefitScheduleLine> lines)
    {
        var l = lines.FirstOrDefault(x => string.Equals(x.BenefitCode, "BaseSumInsured", StringComparison.OrdinalIgnoreCase));
        if (l?.NumericValue is > 0) return l.NumericValue.Value;
        var raw = l?.TextValue ?? l?.Citation ?? "";
        var t = raw.Replace("₹", "").Replace(",", "").Replace("L", "00000").Trim();
        return decimal.TryParse(t, out var v) ? v : 0m;
    }
}
