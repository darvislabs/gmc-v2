using GmcV2.Calculation;
using GmcV2.Core;
using GmcV2.Knowledge;
using Xunit;

namespace GmcV2.Tests;

/// <summary>
/// Acceptance tests for the deterministic calculation + knowledge engines.
/// The pricing test reproduces the workflow §13 showcase EXACTLY
/// (technical ₹1,43,50,000, GST ₹25,83,000, total ₹1,69,33,000, delta ₹1,190/life).
/// </summary>
public class CalculationTests
{
    private static PricingMasterVersion DemoMaster() => new()
    {
        MasterVersionId = 2,
        VersionRef = "PMV-2026Q4-DEMO",
        Status = "Active",
        EffectiveFromUtc = DateTime.UtcNow,
        MedicalTrendByAgeBand = new[] { 8m, 9m, 10.5m },
        CredibilityZ = 0.60m,
        BenchmarkBurnPerLife = 3_237.42m,
        RiskMarginPct = 11m,
        BaseExpensePct = 9m,
        LargeClaimNormalizationPct = 3m,
        StarRejectionPct = 2m,
        CommissionPct = 7.5m,
        GstPct = 18m,
        MaxDiscountPct = 5m,
        SicIncreaseCapturePct = 5.63m,
    };

    private static (AnalyticsSnapshot Snap, List<BenefitScheduleLine> Expiring, List<BenefitScheduleLine> Proposed) DemoCase()
    {
        var snap = new AnalyticsSnapshot
        {
            CoveredLives = 1240,
            ClaimCount = 4180,
            AmountIncurred = 12_883_600m,
            BurnCostPerLife = 10_390m,
            LargeClaimSharePct = 18m,
            AvgAge = 40.27m,
            VolatilityCv = 0.97m,
        };
        var expiring = new List<BenefitScheduleLine>
        {
            new() { ScheduleType = "Expiring", BenefitCode = "BaseSumInsured", NumericValue = 300_000m },
            new() { ScheduleType = "Expiring", BenefitCode = "MaternityNormal", NumericValue = 50_000m },
            new() { ScheduleType = "Expiring", BenefitCode = "MaternityCs", NumericValue = 75_000m },
        };
        var proposed = new List<BenefitScheduleLine>
        {
            new() { ScheduleType = "Proposed", BenefitCode = "BaseSumInsured", NumericValue = 500_000m },
            new() { ScheduleType = "Proposed", BenefitCode = "MaternityNormal", NumericValue = 75_000m },
            new() { ScheduleType = "Proposed", BenefitCode = "MaternityCs", NumericValue = 100_000m },
        };
        return (snap, expiring, proposed);
    }

    [Fact]
    public void Pricing_WorkedExample_ReproducesDocAnchorsExactly()
    {
        var (snap, expiring, proposed) = DemoCase();
        var master = DemoMaster();
        var delta = BenefitDeltaEngine.Compute(expiring, proposed, snap, master, snap.CoveredLives);
        var pricing = PricingEngine.Compute(snap, master, delta, new PricingOverrides());

        Assert.Equal(1_190m, Math.Round(pricing.BenefitDeltaPerLife, 0));
        Assert.Equal(14_350_000m, pricing.TechnicalPremium);
        var beforeGst = pricing.Components.First(c => c.ComponentName == "Premium before GST").Value;
        var gst = pricing.Components.First(c => c.ComponentName.StartsWith("GST")).Value;
        var total = pricing.Components.First(c => c.ComponentName.StartsWith("Total premium")).Value;
        Assert.Equal(25_83_000m, gst);
        Assert.Equal(16_933_000m, total);
        Assert.Equal(pricing.TechnicalPremium, beforeGst); // no commercial discount in the base run
        // bridge carries the full chain: 18 steps, rounding to ₹100 at net+technical
        Assert.True(pricing.Components.Count >= 17, $"bridge steps = {pricing.Components.Count}");
        Assert.Equal(0m, pricing.TechnicalPremium % 100m);
        Assert.True(pricing.TechnicalPremium > 0m);
    }

    [Fact]
    public void Pricing_Overrides_RequireReason_And_MoveInputs()
    {
        var (snap, _, _) = DemoCase();
        var master = DemoMaster();
        var delta = new BenefitDeltaResult();
        Assert.Throws<ArgumentException>(() => PricingEngine.Compute(snap, master, delta,
            new PricingOverrides { MedicalTrendPct = 11.5m }));

        var baseline = PricingEngine.Compute(snap, master, delta, new PricingOverrides());
        var withReason = PricingEngine.Compute(snap, master, delta,
            new PricingOverrides { MedicalTrendPct = 11.5m, ManualLoadingPct = 4m, Reason = "Chronic risk loading (workflow §8.1)" });
        var trendStep = withReason.Components.First(c => c.ComponentName == "Medical trend");
        Assert.True(trendStep.IsOverride);
        Assert.Equal(11.5m, trendStep.Value);
        Assert.Equal("Chronic risk loading (workflow §8.1)", trendStep.OverrideReason);
        Assert.True(withReason.TechnicalPremium > baseline.TechnicalPremium,
            $"override {withReason.TechnicalPremium} !> baseline {baseline.TechnicalPremium}");

        // discount cap: 6% > master 5% must throw
        Assert.Throws<InvalidOperationException>(() => PricingEngine.Compute(snap, master, delta,
            new PricingOverrides { ManualDiscountPct = 6m, Reason = "over cap" }));
    }

    [Fact]
    public void GuardrailEngine_DemoData_AutoQuote_AndFreshNotApplicable()
    {
        var (snap, _, _) = DemoCase();
        var result = GuardrailEngine.Evaluate(snap, GuardrailRules.StandardSet(), dqScore: 96m, mappingConfidence: 99m, "Rollover");
        Assert.True(result.RulesFailed == 0,
            "failing: " + string.Join(", ", result.Evaluations.Where(e => !e.Passed).Select(e => $"{e.RuleCode}:{e.Observed}{e.Operator}{e.Threshold}")));
        Assert.Equal("AutoQuote", result.Outcome);
        Assert.InRange(result.Confidence, 80m, 100m);

        var fresh = GuardrailEngine.Evaluate(new AnalyticsSnapshot { CoveredLives = 400, AvgAge = 35m },
            GuardrailRules.StandardSet(), dqScore: 96m, mappingConfidence: 99m, "Fresh");
        Assert.Contains(fresh.Evaluations, e => e.Severity == "NotApplicable");
        Assert.True(fresh.HardStopCount == 0,
            "fresh hardstops: " + string.Join(", ", fresh.Evaluations.Where(e => e.Severity == "HardStop" && !e.Passed).Select(e => e.RuleCode)));
    }

    [Fact]
    public void GuardrailEngine_FailingRefer_Referred()
    {
        var (snap, _, _) = DemoCase();
        snap.LargeClaimSharePct = 55m; // breaches GRD-LARGE-CLAIM (≤35) and GRD-SINGLE-LARGEST path
        var result = GuardrailEngine.Evaluate(snap, GuardrailRules.StandardSet(), 96m, 99m, "Rollover");
        Assert.Equal("Referred", result.Outcome);
        Assert.Contains(result.Evaluations, e => e.RuleCode == "GRD-LARGE-CLAIM" && !e.Passed);
    }

    [Fact]
    public void Analytics_ComputesCoreMetrics()
    {
        var members = Enumerable.Range(0, 10).Select(i => new StdMember
        { MemberNumber = $"M{i}", Relationship = i < 6 ? "Employee" : "Parent", Age = 30 + i, IsChronic = i % 3 == 0 }).ToList();
        var claims = new List<StdClaim>
        {
            new() { ClaimNumber = "C1", MemberNumber = "M0", ClaimStatus = "Paid", IncurredAmount = 250_000m, IsLargeClaim = true },
            new() { ClaimNumber = "C2", MemberNumber = "M1", ClaimStatus = "Paid", IncurredAmount = 10_000m },
            new() { ClaimNumber = "C3", MemberNumber = "M1", ClaimStatus = "Outstanding", IncurredAmount = 5_000m },
            new() { ClaimNumber = "C4", MemberNumber = "M2", ClaimStatus = "Rejected", IncurredAmount = 0m },
            new() { ClaimNumber = "C5", MemberNumber = "M3", ClaimStatus = "Paid", IncurredAmount = 1_000m, NetworkType = "NonNetwork", IsMaternity = true },
        };
        var snap = AnalyticsEngine.Compute(members, claims);
        Assert.Equal(10, snap.CoveredLives);
        Assert.Equal(6, snap.EmployeeCount);
        Assert.Equal(5, snap.ClaimCount);
        Assert.Equal(266_000m, snap.AmountIncurred);
        Assert.Equal(2, snap.ClaimCount - snap.PaidClaimCount); // 1 outstanding + 1 rejected... rejected excluded from Paid
        Assert.Equal(1, snap.RejectedClaimCount);
        Assert.Equal(26_600m, snap.BurnCostPerLife);
        Assert.InRange(snap.LargeClaimSharePct, 93m, 95m);
        Assert.Equal(40m, snap.ParentSharePct);
        Assert.Equal(40m, snap.ChronicPrevalencePct);
    }

    [Fact]
    public void MappingService_TierChain_EndToEnd()
    {
        // Tier 1 — exact dictionary
        var s1 = MappingService.Suggest("Claim No", new List<ColumnSynonym>(), null, Array.Empty<string>());
        Assert.NotNull(s1);
        Assert.Equal("ClaimNumber", s1!.Value.Target);
        Assert.Equal(1.00m, s1.Value.Confidence);
        Assert.Equal(Enums.TierExactDictionary, s1.Value.Tier);

        // Tier 2 — scoped learned alias beats everything except exact
        var syns = new List<ColumnSynonym>
        {
            new() { SynonymText = "Sanctioned Amt", TargetParameter = "SanctionedAmount", ScopedInsurerTpa = "HealthServe" },
        };
        var scoped = MappingService.Suggest("Sanctioned Amt", syns, "HealthServe", Array.Empty<string>());
        Assert.NotNull(scoped);
        Assert.Equal(Enums.TierLearnedAlias, scoped!.Value.Tier);
        Assert.Equal("SanctionedAmount", scoped.Value.Target);
        var unscoped = MappingService.Suggest("Sanctioned Amt", syns, null, Array.Empty<string>());
        Assert.NotNull(unscoped);
        Assert.NotEqual(Enums.TierLearnedAlias, unscoped!.Value.Tier); // scope mismatch → falls to fuzzy

        // Tier 3 — fuzzy
        var fuzzy = MappingService.Suggest("Claimd Number", new List<ColumnSynonym>(), null, Array.Empty<string>());
        Assert.NotNull(fuzzy);
        Assert.Equal(Enums.TierFuzzy, fuzzy!.Value.Tier);
        Assert.InRange(fuzzy.Value.Confidence, 0.80m, 0.94m);

        // Tier 4 — nothing deterministic → null (caller raises EXC-MAP-LOW-CONF)
        var none = MappingService.Suggest("Zebra Panda Column", new List<ColumnSynonym>(), null, Array.Empty<string>());
        Assert.Null(none);
    }

    [Fact]
    public void CodingService_TierChain_EndToEnd()
    {
        var diseases = DiseaseSeed.Default();
        var golden = new List<GoldenEntry>
        {
            new() { RawTextNorm = "k/c/o DM with foot ulcer", IcdLevel1 = "E11", IcdLevel2 = "E11.9", WasCorrected = true },
        };
        var svc = new CodingService(diseases, golden);

        // golden beats master on exact normalized text
        var g = svc.CodeOne(1, "K/C/O DM with foot ulcer");
        Assert.Equal(Enums.TierGolden, g.Tier);
        Assert.Equal("E11.9", g.IcdLevel2);

        // master exact name
        var m = svc.CodeOne(2, "Type 2 Diabetes Mellitus");
        Assert.Equal(Enums.TierMaster, m.Tier);
        Assert.Equal("E11.9", m.IcdLevel2);
        Assert.False(m.NeedsReview);

        // keyword with abbreviation expansion → master keyword, review band
        var k = svc.CodeOne(3, "k/c/o htn since 2015");
        Assert.Equal("I10", k.IcdLevel1);
        Assert.True(k.NeedsReview);

        // 'ca breast' → C50.9 but 'post cabg' must NOT hit carcinoma
        var cabg = svc.CodeOne(4, "post cabg status");
        Assert.NotEqual("C50", cabg.IcdLevel1);
        var breast = svc.CodeOne(5, "ca breast");
        Assert.Equal("C50.9", breast.IcdLevel2);

        // maternity pair
        Assert.Equal("O80", svc.CodeOne(6, "normal delivery").IcdLevel1);
        Assert.Equal("O82", svc.CodeOne(7, "lower segment caesarean section").IcdLevel1);

        // unknown → unmapped review
        var u = svc.CodeOne(8, "quantum entanglement of the left knee");
        Assert.Null(u.IcdLevel1);
        Assert.True(u.NeedsReview);

        var outcome = svc.CodeMany(Enumerable.Range(0, 10).Select(i => (i, i % 2 == 0 ? "Type 2 Diabetes Mellitus" : "senile cataract")));
        Assert.Equal(10, outcome.Decisions.Count);
        Assert.Equal(0, outcome.ReviewCount);
    }

    [Fact]
    public void StandardizationEngine_Statuses_Amounts_Dq()
    {
        var columns = new List<SourceColumn>
        {
            new() { SourceColumnId = 1, DocumentId = 9, ColumnIndex = 0, Header = "Claim No" },
            new() { SourceColumnId = 2, DocumentId = 9, ColumnIndex = 1, Header = "Member ID" },
            new() { SourceColumnId = 3, DocumentId = 9, ColumnIndex = 2, Header = "Claim Status" },
            new() { SourceColumnId = 4, DocumentId = 9, ColumnIndex = 3, Header = "Sanctioned Amt" },
            new() { SourceColumnId = 5, DocumentId = 9, ColumnIndex = 4, Header = "Paid Amt" },
            new() { SourceColumnId = 6, DocumentId = 9, ColumnIndex = 5, Header = "Date of Admission" },
        };
        var mappings = columns.ToDictionary(
            c => c.SourceColumnId,
            c => c.Header switch
            {
                "Claim No" => new ColumnMapping { SourceColumnId = 1, TargetParameter = "ClaimNumber", Confidence = 1m, ProvenanceTier = "ExactDictionary" },
                "Member ID" => new ColumnMapping { SourceColumnId = 2, TargetParameter = "MemberNumber", Confidence = 1m, ProvenanceTier = "ExactDictionary" },
                "Claim Status" => new ColumnMapping { SourceColumnId = 3, TargetParameter = "ClaimStatus", Confidence = 1m, ProvenanceTier = "ExactDictionary" },
                "Sanctioned Amt" => new ColumnMapping { SourceColumnId = 4, TargetParameter = "SanctionedAmount", Confidence = 1m, ProvenanceTier = "LearnedAlias" },
                "Paid Amt" => new ColumnMapping { SourceColumnId = 5, TargetParameter = "PaidAmount", Confidence = 1m, ProvenanceTier = "ExactDictionary" },
                _ => new ColumnMapping { SourceColumnId = 6, TargetParameter = "AdmissionDate", Confidence = 1m, ProvenanceTier = "ExactDictionary" },
            });
        var plan = StandardizationEngine.BuildPlan(columns, mappings);
        var paidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["Paid"] = "Paid", ["Outstanding"] = "Outstanding", ["Pending"] = "Outstanding", ["Rejected"] = "Rejected" };
        var rows = new List<string[]>
        {
            new[] { "Claim No", "Member ID", "Claim Status", "Sanctioned Amt", "Paid Amt", "Date of Admission" },
            new[] { "C1", "M1", "Pending", "120000.50", "1,00,000", "2025-04-01" },
            new[] { "C2", "M2", "Rejected", "50000", "0", "01/05/2025" },
            new[] { "C3", "M3", "Paid", "300000", "295000", "2025-06-11" },
        };
        var (claims, dq) = StandardizationEngine.StandardizeClaims(rows, plan, paidMap);
        Assert.Equal(3, claims.Count);
        Assert.Equal("Outstanding", claims[0].ClaimStatus);
        Assert.Equal(120000.50m, claims[0].SanctionedAmount);
        Assert.Equal(100000m, claims[0].PaidAmount);
        Assert.Equal(20000.50m, claims[0].OutstandingAmount);
        Assert.Equal(0m, claims[1].IncurredAmount); // rejected
        Assert.True(claims[2].IsLargeClaim);        // ≥ ₹2L
        Assert.Equal(new DateTime(2025, 4, 1), claims[0].AdmissionDate);
        Assert.Equal(new DateTime(2025, 5, 1), claims[1].AdmissionDate);
        Assert.Contains(dq, d => d.RuleCode == "DQ-CLAIM-LINKAGE" && d.Passed);
    }

    [Fact]
    public void MasterResolver_SeedDefault_HasAllDefaults()
    {
        var m = MasterResolver.SeedDefault();
        Assert.Equal("PMV-2026Q4-SEED", m.VersionRef);
        Assert.Equal(0.60m, m.CredibilityZ);
        Assert.Equal(19_000m, m.BenchmarkBurnPerLife);
        Assert.Equal(60m, m.SicIncreaseCapturePct);
        Assert.Equal(18m, m.GstPct);
        Assert.Equal(3, m.MedicalTrendByAgeBand.Length);
    }
}
