namespace GmcV2.Core;

/// <summary>Per-case SI-impact scenario (uw.SIScenario). Editable realization factors +
/// apply-to-pricing flag; computed increments are cached at save time.</summary>
public sealed class SIScenarioRow
{
    public long CaseId { get; set; }
    public decimal Factor3To4 { get; set; } = 1m;
    public decimal Factor3To5 { get; set; } = 1m;
    public decimal Factor4To5 { get; set; } = 1m;
    public bool ApplyToPricing { get; set; }
    public decimal Inc3To4 { get; set; }
    public decimal Inc3To5 { get; set; }
    public decimal Inc4To5 { get; set; }
    public decimal IncrementalPerLife { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
