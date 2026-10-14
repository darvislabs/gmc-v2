using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>Multi-year company experience (prototype §5 "3-Year Company Experience"):
/// buckets standardized claims + member exposure by Indian fiscal policy year (Apr–Mar)
/// and produces enrollment exposure, paid claims, frequency and burn cost per year.</summary>
public static class MultiYearEngine
{
    public sealed class PolicyYearRow
    {
        public string PolicyYear { get; set; } = "";
        public int PaidClaims { get; set; }
        public int TotalClaims { get; set; }
        public decimal PaidAmount { get; set; }
        public int Lives { get; set; }
        public decimal FrequencyPct { get; set; }
        public decimal BurnCostPerLife { get; set; }
    }

    /// <summary>Indian FY label for a date: Apr–Mar → "2024-25".</summary>
    public static string FiscalYear(DateTime? d)
    {
        if (d is null) return "Unknown";
        var y = d.Value.Year;
        return d.Value.Month >= 4 ? $"{y}-{(y + 1) % 100:D2}" : $"{y - 1}-{y % 100:D2}";
    }

    public static List<PolicyYearRow> Compute(IReadOnlyList<StdClaim> claims, int memberCount)
    {
        // Exposure lives: members count once per fiscal year in which they were enrolled;
        // without enrollment dates we approximate exposure with the current census (latest year only).
        var lives = Math.Max(memberCount, 1);
        var rows = claims
            .GroupBy(c => FiscalYear(c.AdmissionDate))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var paid = g.Where(c => c.ClaimStatus == "Paid").ToList();
                var paidAmt = paid.Sum(c => c.PaidAmount);
                return new PolicyYearRow
                {
                    PolicyYear = g.Key,
                    PaidClaims = paid.Count,
                    TotalClaims = g.Count(),
                    PaidAmount = paidAmt,
                    Lives = lives,
                    FrequencyPct = Math.Round(100m * paid.Count / lives, 2),
                    BurnCostPerLife = Math.Round(paidAmt / lives, 2),
                };
            })
            .ToList();

        // Fill the 3 prototype years even when a year has no claims
        for (var back = 2; back >= 0; back--)
        {
            var fy = FiscalYear(new DateTime(DateTime.Today.Year - back, DateTime.Today.Month, 1));
            if (!rows.Any(r => r.PolicyYear == fy))
                rows.Add(new PolicyYearRow { PolicyYear = fy, Lives = lives });
        }
        return rows.OrderBy(r => r.PolicyYear == "Unknown" ? "9999" : r.PolicyYear).ToList();
    }
}
