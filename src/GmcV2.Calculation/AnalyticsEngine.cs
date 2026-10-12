using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// Stage 7 — deterministic experience analytics (db.md §8.1).
/// EngineBridge binds this via reflection: static Compute(List<StdMember>, List<StdClaim>)
/// → AnalyticsSnapshot. Class is non-static only because EngineBridge.Activator
/// requires an instantiable type; all behavior is through static methods.
/// </summary>
public sealed class AnalyticsEngine
{
    public AnalyticsEngine() { }

    /// <summary>Bridge entry point — exact signature probed by EngineBridge.ComputeAnalytics.</summary>
    public static AnalyticsSnapshot Compute(List<StdMember> members, List<StdClaim> claims)
    {
        var lives = members.Count;
        var employees = members.Count(m => m.Relationship == "Employee");
        var incurred = claims.Sum(c => c.IncurredAmount);
        var paid = claims.Where(c => c.ClaimStatus == "Paid").Sum(c => c.IncurredAmount);
        var outstanding = claims.Where(c => c.ClaimStatus == "Outstanding").Sum(c => c.IncurredAmount);
        var rejectedCount = claims.Count(c => c.ClaimStatus == "Rejected");
        var sizes = claims.Select(c => c.IncurredAmount).OrderBy(x => x).ToList();
        var median = sizes.Count == 0 ? 0m : sizes.Count % 2 == 1
            ? sizes[sizes.Count / 2]
            : (sizes[sizes.Count / 2 - 1] + sizes[sizes.Count / 2]) / 2m;

        var top5 = sizes.Count == 0 ? 0m : sizes.TakeLast(5).Sum() / Math.Max(incurred, 0.01m) * 100m;
        var largest = sizes.Count == 0 ? 0m : sizes.Max() / Math.Max(incurred, 0.01m) * 100m;

        var largeCost = claims.Where(c => c.IsLargeClaim).Sum(c => c.IncurredAmount);
        var maternityCost = claims.Where(c => c.IsMaternity).Sum(c => c.IncurredAmount);
        var nonNetworkCost = claims.Where(c => c.NetworkType == "NonNetwork").Sum(c => c.IncurredAmount);

        var parents = members.Count(m => m.IsParent);
        var seniors = members.Count(m => (m.Age ?? 0) >= 55);
        var chronic = members.Count(m => m.IsChronic);
        var claimants = claims.Select(c => c.MemberNumber).Distinct().Count();

        var byAgeBand = members.GroupBy(m => m.Age switch
        {
            null => "Unknown",
            < 18 => "0-17",
            < 26 => "18-25",
            < 36 => "26-35",
            < 46 => "36-45",
            < 56 => "46-55",
            < 66 => "56-65",
            _ => "66+",
        }).ToDictionary(g => g.Key, g => (decimal)g.Count() / Math.Max(lives, 1) * 100m);

        var byRel = members.GroupBy(m => m.Relationship)
            .ToDictionary(g => g.Key, g => (decimal)g.Count() / Math.Max(lives, 1) * 100m);
        var byDisease = claims.GroupBy(c => c.DiseaseCategory ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.IncurredAmount) / Math.Max(incurred, 0.01m) * 100m);

        // Volatility = CV of incurred cost PER MEMBER (experience volatility) — the
        // guardrail is about experience stability, not per-row heavy tails (db §8.1).
        var perMember = claims.GroupBy(c => c.MemberNumber).Select(g => g.Sum(x => x.IncurredAmount)).ToList();
        decimal memberMean = perMember.Count == 0 ? 0m : perMember.Average();
        var memberVar = perMember.Count == 0 ? 0m : perMember.Sum(x => (x - memberMean) * (x - memberMean)) / perMember.Count;
        var memberCv = memberMean > 0 ? (decimal)Math.Sqrt((double)(memberVar / (memberMean * memberMean))) : 0m;

        return new AnalyticsSnapshot
        {
            CoveredLives = lives,
            EmployeeCount = employees,
            ClaimCount = claims.Count,
            PaidClaimCount = claims.Count(c => c.ClaimStatus == "Paid"),
            RejectedClaimCount = rejectedCount,
            AmountClaimed = incurred,
            AmountIncurred = incurred,
            AmountPaid = paid,
            AmountOutstanding = outstanding,
            AmountSanctioned = incurred,
            AmountApproved = incurred,
            FrequencyPer100 = lives > 0 ? Math.Round((decimal)claims.Count / lives * 100m, 4) : 0m,
            ClaimantIncidencePct = lives > 0 ? Math.Round((decimal)claimants / lives * 100m, 4) : 0m,
            AvgClaimSize = claims.Count > 0 ? Math.Round(incurred / claims.Count, 2) : 0m,
            MedianClaimSize = Math.Round(median, 2),
            CostPerLife = lives > 0 ? Math.Round(incurred / lives, 2) : 0m,
            BurnCostPerLife = lives > 0 ? Math.Round(incurred / lives, 2) : 0m,
            LargeClaimSharePct = Math.Round(largeCost / Math.Max(incurred, 0.01m) * 100m, 4),
            Top5ClaimSharePct = Math.Round(top5, 4),
            SingleLargestSharePct = Math.Round(largest, 4),
            VolatilityCv = Math.Round(memberCv, 4),
            ChronicPrevalencePct = lives > 0 ? Math.Round((decimal)chronic / lives * 100m, 4) : 0m,
            MaternitySharePct = Math.Round(maternityCost / Math.Max(incurred, 0.01m) * 100m, 4),
            NonNetworkSharePct = Math.Round(nonNetworkCost / Math.Max(incurred, 0.01m) * 100m, 4),
            ParentSharePct = lives > 0 ? Math.Round((decimal)parents / lives * 100m, 4) : 0m,
            SeniorAgeSharePct = lives > 0 ? Math.Round((decimal)seniors / lives * 100m, 4) : 0m,
            AvgAge = lives > 0 ? Math.Round((decimal)members.Average(m => m.Age ?? 0), 2) : 0m,
            ByAgeBand = byAgeBand,
            ByRelationship = byRel,
            ByDiseaseGroup = byDisease,
        };
    }
}
