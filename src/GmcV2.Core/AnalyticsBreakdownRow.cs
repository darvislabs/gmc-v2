using GmcV2.Core;

namespace GmcV2.Core;

/// <summary>One row of uw.AnalyticsBreakdown — a dimension slice of the analytics snapshot
/// (AgeBand / Relationship / DiseaseGroup / SI-Band / Department).</summary>
public sealed class AnalyticsBreakdownRow
{
    public long AnalyticsBreakdownId { get; set; }
    public long AnalyticsSnapshotId { get; set; }
    public string Dimension { get; set; } = "";
    public string DimensionValue { get; set; } = "";
    public int Lives { get; set; }
    public int ClaimCount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal IncurredAmount { get; set; }
    public decimal FrequencyPer100 { get; set; }
    public decimal CostSharePct { get; set; }
    public decimal AverageClaimAmount { get; set; }
}
