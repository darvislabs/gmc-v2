namespace GmcV2.Core;

/// <summary>Standardized member row (ing.StdMember). Deterministic projection of raw enrollment.</summary>
public sealed class StdMember
{
    public long StdMemberId { get; set; }
    public string MemberNumber { get; set; } = "";
    public string Relationship { get; set; } = "Employee"; // Employee | Spouse | Child | Parent
    public string Gender { get; set; } = "";
    public int? Age { get; set; }
    public string? AgeBand { get; set; }
    public string City { get; set; } = "";
    public string? Department { get; set; }
    public bool IsDependent => Relationship != "Employee";
    public bool IsParent => Relationship == "Parent";
    public bool IsChronic { get; set; }
}

/// <summary>Standardized claim row (ing.StdClaim).</summary>
public sealed class StdClaim
{
    public long StdClaimId { get; set; }
    public long CaseId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public string MemberNumber { get; set; } = "";
    public string ClaimStatus { get; set; } = "Paid";   // Paid | Outstanding | Closed | Rejected
    public string? DiagnosisText { get; set; }
    public bool IsChronic { get; set; }
    public DateTime? AdmissionDate { get; set; }
    public DateTime? DischargeDate { get; set; }
    public decimal ClaimedAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public decimal SanctionedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal IncurredAmount { get; set; }
    public string NetworkType { get; set; } = "Network";  // Network | NonNetwork
    public string? DiseaseCategory { get; set; }
    public string? IcdLevel1 { get; set; }
    public string? IcdLevel2 { get; set; }
    public decimal CodingConfidence { get; set; } = 1m;
    public bool IsLargeClaim { get; set; }
    public bool IsDuplicateFlag { get; set; }
    public bool IsMaternity { get; set; }
}

/// <summary>Immutable per-case analytics snapshot (uw.AnalyticsSnapshot.MetricsJson shape).</summary>
public sealed class AnalyticsSnapshot
{
    public int CoveredLives { get; set; }
    public int EmployeeCount { get; set; }
    public int ClaimCount { get; set; }
    public int PaidClaimCount { get; set; }
    public int RejectedClaimCount { get; set; }
    public decimal AmountClaimed { get; set; }
    public decimal AmountApproved { get; set; }
    public decimal AmountSanctioned { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountOutstanding { get; set; }
    public decimal AmountIncurred { get; set; }
    public decimal IncurredClaimsRatioPct { get; set; }
    public decimal FrequencyPer100 { get; set; }
    public decimal ClaimantIncidencePct { get; set; }
    public decimal AvgClaimSize { get; set; }
    public decimal MedianClaimSize { get; set; }
    public decimal CostPerLife { get; set; }
    public decimal BurnCostPerLife { get; set; }
    public decimal LargeClaimSharePct { get; set; }
    public decimal Top5ClaimSharePct { get; set; }
    public decimal SingleLargestSharePct { get; set; }
    public decimal VolatilityCv { get; set; }
    public decimal ChronicPrevalencePct { get; set; }
    public decimal MaternitySharePct { get; set; }
    public decimal NonNetworkSharePct { get; set; }
    public decimal ParentSharePct { get; set; }
    public decimal SeniorAgeSharePct { get; set; } // 55+
    public decimal AvgAge { get; set; }
    public string? TrendNarrative { get; set; } // AI trend summary (§5 LLM panel); deterministic summary when AI off
    public Dictionary<string, decimal> ByAgeBand { get; set; } = new();
    public Dictionary<string, decimal> ByRelationship { get; set; } = new();
    public Dictionary<string, decimal> ByDiseaseGroup { get; set; } = new();
}

/// <summary>One bridge step of the pricing chain (uw.PricingComponent row).</summary>
public sealed class PricingComponent
{
    public int StepOrder { get; set; }
    public string ComponentName { get; set; } = "";
    public string SourceType { get; set; } = Enums.SrcComputed;
    public string? MasterVersionRef { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";      // % | ₹/life | ₹ | years | ratio
    public string? Basis { get; set; }
    public bool IsOverride { get; set; }
    public string? OverrideReason { get; set; }
}

/// <summary>Full result of the deterministic pricing chain (uw.PricingRun).</summary>
public sealed class PricingResult
{
    public long PricingRunId { get; set; }
    public int MasterVersionId { get; set; }
    public string MasterVersionRef { get; set; } = "";
    public List<PricingComponent> Components { get; set; } = new();
    public decimal ObservedBurnPerLife { get; set; }
    public decimal NormalizedBurnPerLife { get; set; }
    public decimal UltimateBurnPerLife { get; set; }
    public decimal TrendedBurnPerLife { get; set; }
    public decimal BenefitDeltaPerLife { get; set; }
    public decimal NetTechnicalPerLife { get; set; }
    public decimal TechnicalPremium { get; set; }
    public decimal LoadingsPerLife { get; set; }
    public decimal GrossPremiumPerLife { get; set; }
    public decimal GrossPremium { get; set; }
    public string InputSnapshotJson { get; set; } = "";
    public string InputsHash { get; set; } = "";
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class GuardrailEvaluation
{
    public string RuleCode { get; set; } = "";
    public string Category { get; set; } = "";
    public string Operator { get; set; } = "";
    public decimal Threshold { get; set; }
    public decimal Observed { get; set; }
    public bool Passed { get; set; }
    public string Severity { get; set; } = "Refer";
    public string Message { get; set; } = "";
}

public sealed class GuardrailResult
{
    public List<GuardrailEvaluation> Evaluations { get; set; } = new();
    public int RulesPassed => Evaluations.Count(e => e.Passed);
    public int RulesFailed => Evaluations.Count(e => !e.Passed);
    public int ReferCount => Evaluations.Count(e => !e.Passed && e.Severity == "Refer");
    public int HardStopCount => Evaluations.Count(e => !e.Passed && e.Severity == "HardStop");
    public decimal Confidence { get; set; }
    public string Outcome { get; set; } = "AutoQuote";  // AutoQuote | Referred
}
