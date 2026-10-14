using GmcV2.Calculation;
using Xunit;

public class SIScenarioTests
{
    [Fact]
    public void Compute_ChronicOnly_Replays_Bands_With_Factors()
    {
        var claims = new List<(decimal, bool, string?)>
        {
            (300_000m, true, "E11"), (200_000m, true, "I10"), (50_000m, false, "J45"),
        };
        var r = SIScenarioEngine.Compute(claims, siFactorPct: 1.8m);
        Assert.Equal(2, r.ChronicClaims);
        Assert.Equal(3, r.ByBand.Count);
        Assert.Equal(0m, r.IncrementalPerLife);            // not applied
        Assert.Equal(r.ByBand[1].IncrementalCost, r.Inc3To5);
        // 3→5 is two band steps: exactly 2× the single-step cost at factor 1
        Assert.Equal(2 * r.ByBand[0].IncrementalCost, r.ByBand[1].IncrementalCost);
    }

    [Fact]
    public void Compute_Applied_Yields_PerLife_And_Respects_Factors()
    {
        var claims = new List<(decimal, bool, string?)> { (100_000m, true, "N18") };
        var r = SIScenarioEngine.Compute(claims, 1.8m, factor3To4: 1.5m, factor4To5: 0.5m, applyToPricing: true);
        Assert.Equal(1.5m, r.Factor3To4);
        Assert.True(r.IncrementalPerLife > 0);
        Assert.Equal(r.ByBand[0].IncrementalCost, r.Inc3To4);
        // zero-chronic book must produce zero impact
        var empty = SIScenarioEngine.Compute(new List<(decimal, bool, string?)>(), 1.8m, applyToPricing: true);
        Assert.Equal(0m, empty.IncrementalPerLife);
    }
}
