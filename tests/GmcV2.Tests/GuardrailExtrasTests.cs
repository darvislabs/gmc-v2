using GmcV2.Calculation;
using GmcV2.Core;
using Xunit;

public class GuardrailExtrasTests
{
    private static AnalyticsSnapshot Snap() => new()
    {
        CoveredLives = 1000, AmountIncurred = 5_000_000m, BurnCostPerLife = 5000m,
        ByDiseaseGroup = new Dictionary<string, decimal> { ["Cardiac"] = 30m, ["Maternity"] = 10m },
    };
    private static List<BenefitScheduleLine> Sched(string type, decimal si) => new()
    {
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "BaseSumInsured", NumericValue = si },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "MaternityNormal", NumericValue = 65000m },
    };

    [Fact]
    public void SiIncrease_Computed_From_Schedules()
    {
        var ex = GuardrailExtras.From(Snap(), Sched("Expiring", 300_000m), Sched("Proposed", 500_000m));
        Assert.NotNull(ex.SiIncreasePct);
        Assert.Equal(66.6667m, Math.Round(ex.SiIncreasePct!.Value, 4));
    }

    [Fact]
    public void Concentration_Is_Max_Disease_Share()
    {
        var ex = GuardrailExtras.From(Snap(), Sched("Expiring", 300_000m), Sched("Proposed", 300_000m));
        Assert.Equal(30m, ex.DiseaseGroupConcentrationPct);
    }

    [Fact]
    public void NonStandard_Counts_Unknown_Codes()
    {
        var proposed = Sched("Proposed", 500_000m);
        proposed.Add(new BenefitScheduleLine { ScheduleType = "Proposed", BenefitCode = "ExoticWellnessSpa", NumericValue = 5000m });
        proposed.Add(new BenefitScheduleLine { ScheduleType = "Proposed", BenefitCode = "PetInsurance", NumericValue = 2000m });
        var ex = GuardrailExtras.From(Snap(), Sched("Expiring", 300_000m), proposed);
        Assert.Equal(2, ex.NonStandardBenefitCount);
    }

    [Fact]
    public void SiIncrease_Rule_Refer_At_100pct_Cap()
    {
        var snap = Snap();
        var rules = new List<GuardrailRuleDef>
        {
            new() { RuleCode = "GRD-SI-INCREASE", Category = "Commercial", MetricSelector = "SiIncreasePct",
                    Operator = "<=", Threshold = 100m, Severity = "Refer",
                    MessageTemplate = "SI increase {observed}%", AppliesToExperienceBusiness = false },
        };
        var over = GuardrailEngine.Evaluate(snap, rules, 96m, 99m, "Rollover",
            GuardrailExtras.From(snap, Sched("Expiring", 200_000m), Sched("Proposed", 500_000m)));
        Assert.Contains(over.Evaluations, e => e.RuleCode == "GRD-SI-INCREASE" && !e.Passed);
        var within = GuardrailEngine.Evaluate(snap, rules, 96m, 99m, "Rollover",
            GuardrailExtras.From(snap, Sched("Expiring", 400_000m), Sched("Proposed", 500_000m)));
        Assert.Contains(within.Evaluations, e => e.RuleCode == "GRD-SI-INCREASE" && e.Passed);
    }
}
