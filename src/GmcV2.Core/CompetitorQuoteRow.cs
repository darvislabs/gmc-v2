using GmcV2.Core;

namespace GmcV2.Core;

/// <summary>Read model of uw.CompetitorQuote (prototype §8 competition premium summary).</summary>
public sealed class CompetitorQuoteRow
{
    public long CompetitorQuoteId { get; set; }
    public long CaseId { get; set; }
    public string Name { get; set; } = "";
    public int CoveredLives { get; set; }
    public decimal SumInsured { get; set; }
    public decimal Premium { get; set; }
    public decimal RatePerLife { get; set; }
    public decimal ExtractionConfidence { get; set; }
    public decimal? DeltaPct { get; set; } // computed vs our current quote rate
}
