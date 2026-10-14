namespace GmcV2.Worker;

/// <summary>
/// Valid column → parameter mapping targets (workflow §6.2 / tech §5.3) — the canonical
/// vocabulary consumed by StandardizationEngine.BuildPlan and rendered by the Mapping
/// Workbench retarget drawer. Harvested from StageHandlers.MapHeaderToParameter's values
/// (dictionary tier) plus the extra parameters the standardization engine reads from real
/// TPA claims/enrollment sheets.
/// </summary>
public static class MappingParameters
{
    public static readonly string[] All =
    {
        // StageHandlers.MapHeaderToParameter dictionary values
        "ClaimNumber", "MemberNumber", "ClaimStatus", "SanctionedAmount", "IsLargeClaim",
        "DiagnosisText", "Relationship", "Gender", "Age", "HasChronic",
        // read by StandardizationEngine from real TPA sheets (db §5.5)
        "ClaimedAmount", "ApprovedAmount", "PaidAmount", "AdmissionDate", "DischargeDate",
        "NetworkType", "DateOfBirth",
    };

    /// <summary>Parameters whose absence fails DQ-REQUIRED-FIELDS (mirror of StageHandlers.IsRequiredParameter).</summary>
    public static readonly HashSet<string> Required = new()
    { "ClaimNumber", "MemberNumber", "ClaimStatus", "SanctionedAmount", "Relationship", "Age" };

    public static bool IsRequired(string p) => Required.Contains(p);
}
