namespace GmcV2.Core;

/// <summary>Row of a benefit schedule — expiring, proposed or competitor (uw.BenefitScheduleLine).</summary>
public sealed class BenefitScheduleLine
{
    public string ScheduleType { get; set; } = "Expiring"; // Expiring | Proposed | Competitor
    public string BenefitCode { get; set; } = "";   // BaseSumInsured, MaternityNormal, MaternityCs, Ambulance, Newborn, CorporateBuffer, RoomRent, Copay, PedDay1, DiseaseSubLimit
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public decimal Confidence { get; set; } = 1m;
    public string? Citation { get; set; }
}

/// <summary>One costed benefit delta line (uw.BenefitDeltaLine). DeltaPerLife feeds pricing.</summary>
public sealed class BenefitDeltaLine
{
    public string BenefitCode { get; set; } = "";
    public string BenefitName { get; set; } = "";
    public decimal? ExpiringValue { get; set; }
    public decimal? ProposedValue { get; set; }
    public string Method { get; set; } = "Master";  // Experience | Master | Blended | Factor | NotApplicable
    public decimal ExperienceCredibilityPct { get; set; }
    public decimal DeltaPerLife { get; set; }
    public string Rationale { get; set; } = "";
}

/// <summary>Deterministic benefit-delta computation result.</summary>
public sealed class BenefitDeltaResult
{
    public List<BenefitDeltaLine> Lines { get; set; } = new();
    public decimal TotalDeltaPerLife => Lines.Sum(l => l.DeltaPerLife);
}

/// <summary>Versioned, effective-dated pricing master set (uw.PricingMasterVersion + bands).</summary>
public sealed class PricingMasterVersion
{
    public int MasterVersionId { get; set; }
    public string VersionRef { get; set; } = "";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string Status { get; set; } = "Active";   // Draft | Active | Superseded

    // PricingBand rows (db §9.2) — one row per band type
    public decimal[] MedicalTrendByAgeBand { get; set; } = { 8m, 9m, 10.5m };        // ≤45, 46-60, 60+
    public decimal IbnrPctByClaimBand { get; set; } = 5m;                            // default mid band
    public decimal CredibilityZ { get; set; } = 0.60m;
    public decimal BenchmarkBurnPerLife { get; set; } = 19_000m;
    public decimal RiskMarginPct { get; set; } = 11m;
    public decimal BaseExpensePct { get; set; } = 9m;
    public decimal LargeClaimNormalizationPct { get; set; } = 3m;
    public decimal StarRejectionPct { get; set; } = 2m;
    public decimal CommissionPct { get; set; } = 7.5m;
    public decimal GstPct { get; set; } = 18m;
    public decimal MaxCommissionPct { get; set; } = 15m;
    public decimal MaxDiscountPct { get; set; } = 5m;
    /// <summary>Factor-grid capture for sum-insured increases (workflow §9 benefit costing).
    /// Portfolio seed = 60% (INFRW parity). Demo-cohort masters may pin 13.5% to match
    /// the §13 worked example. Additive field — 0/absent = legacy 60% behavior.</summary>
    public decimal SicIncreaseCapturePct { get; set; } = 60m;
}

/// <summary>Underwriter pricing overrides — replace inputs, never formula (workflow §7.2).</summary>
public sealed class PricingOverrides
{
    public decimal? MedicalTrendPct { get; set; }
    public decimal? IbnrPct { get; set; }
    public decimal? CredibilityZ { get; set; }
    public decimal? BenchmarkBurnPerLife { get; set; }
    public decimal? RiskMarginPct { get; set; }
    public decimal? LargeClaimNormalizationPct { get; set; }
    public decimal? StarRejectionPct { get; set; }
    public decimal? ManualLoadingPct { get; set; }
    public decimal? ManualDiscountPct { get; set; }
    public decimal? BaseExpensePct { get; set; }
    public decimal? CommissionPct { get; set; }
    /// <summary>Mandatory when any override is set.</summary>
    public string? Reason { get; set; }
    public bool HasAny => MedicalTrendPct is not null || IbnrPct is not null || CredibilityZ is not null
        || BenchmarkBurnPerLife is not null || RiskMarginPct is not null || LargeClaimNormalizationPct is not null
        || StarRejectionPct is not null || ManualLoadingPct is not null || ManualDiscountPct is not null
        || BaseExpensePct is not null || CommissionPct is not null;
}

/// <summary>Resolved guardrail rule set (uw.GuardrailRule + Threshold).</summary>
public sealed class GuardrailRuleDef
{
    public string RuleCode { get; set; } = "";
    public string? RuleName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Category { get; set; } = "";
    /// <summary>Selector into the analytics snapshot, e.g. "CoveredLives".</summary>
    public string MetricSelector { get; set; } = "";
    public string Operator { get; set; } = ">=";    // >= | <= | < | >
    public decimal Threshold { get; set; }
    public string Severity { get; set; } = "Refer"; // Refer | HardStop
    public string MessageTemplate { get; set; } = "";
    public bool AppliesToExperienceBusiness { get; set; } = true;
    /// <summary>For ratio rules: denominator metric (defaults CoveredLives/ClaimCount context).</summary>
    public string? PctOfMetric { get; set; }
}
