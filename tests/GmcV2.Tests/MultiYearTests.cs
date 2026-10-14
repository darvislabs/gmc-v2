using GmcV2.Core;
using Xunit;

public class MultiYearTests
{
    private static StdClaim Claim(string no, string status, decimal paid, int y, int m, int d) => new()
    { ClaimNumber = no, MemberNumber = "M1", ClaimStatus = status, PaidAmount = paid, AdmissionDate = new DateTime(y, m, d) };

    [Fact]
    public void FiscalYear_AprStart_BucketsCorrectly()
    {
        Assert.Equal("2024-25", GmcV2.Infrastructure.MultiYearEngine.FiscalYear(new DateTime(2024, 4, 1)));
        Assert.Equal("2024-25", GmcV2.Infrastructure.MultiYearEngine.FiscalYear(new DateTime(2025, 3, 31)));
        Assert.Equal("2025-26", GmcV2.Infrastructure.MultiYearEngine.FiscalYear(new DateTime(2025, 4, 1)));
    }

    [Fact]
    public void Compute_BucketsClaimsByFiscalYear()
    {
        var claims = new List<StdClaim>
        {
            Claim("C1", "Paid", 1000m, 2024, 8, 15),
            Claim("C2", "Paid", 2000m, 2024, 12, 3),
            Claim("C3", "Outstanding", 500m, 2025, 1, 10),
            Claim("C4", "Paid", 3000m, 2025, 6, 2),
        };
        var rows = GmcV2.Infrastructure.MultiYearEngine.Compute(claims, 100);
        var y1 = rows.Single(r => r.PolicyYear == "2024-25");
        var y2 = rows.Single(r => r.PolicyYear == "2025-26");
        Assert.Equal(2, y1.PaidClaims);          // C1 + C2 paid; C3 outstanding
        Assert.Equal(3, y1.TotalClaims);
        Assert.Equal(3000m, y1.PaidAmount);
        Assert.Equal(100, y1.Lives);
        Assert.Equal(30m, y1.BurnCostPerLife);   // 3000/100
        Assert.Equal(1, y2.PaidClaims);
        Assert.Equal(3000m, y2.PaidAmount);
        // all 3 prototype years present even with zero claims
        Assert.True(rows.Count(r => r.PolicyYear != "Unknown") >= 3);
    }
}
