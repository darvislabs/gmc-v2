using GmcV2.Core;

namespace GmcV2.Core;

/// <summary>Read/write model of uw.BenefitCostingMaster (prototype §7 UW master structure).</summary>
public sealed class BenefitCostingRow
{
    public long BenefitCostingMasterId { get; set; }
    public int MasterVersionId { get; set; }
    public string BenefitCode { get; set; } = "";
    public decimal UtilizationRate { get; set; }
    public decimal SeverityCaptureRate { get; set; }
    public decimal IncidenceRate { get; set; }
    public decimal SeverityAmount { get; set; }
    public decimal FactorPct { get; set; }
    public decimal ExperienceCredibilityPct { get; set; }
    public decimal AttributionFactor { get; set; } = 1m;
}

/// <summary>Read/write model of uw.CommissionMaster.</summary>
public sealed class CommissionRow
{
    public long CommissionMasterId { get; set; }
    public int MasterVersionId { get; set; }
    public string ChannelType { get; set; } = "Default";
    public decimal DefaultRatePct { get; set; } = 7.5m;
    public decimal MinRatePct { get; set; }
    public decimal MaxRatePct { get; set; } = 15m;
    public bool IsActive { get; set; } = true;
}

/// <summary>Read/write model of uw.StandardLoadingMaster.</summary>
public sealed class StandardLoadingRow
{
    public long StandardLoadingMasterId { get; set; }
    public int MasterVersionId { get; set; }
    public string LoadingName { get; set; } = "";
    public decimal Value { get; set; }
}
