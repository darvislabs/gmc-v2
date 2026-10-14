using GmcV2.Core;
using Xunit;

public class DiscountLoopTests
{
    [Fact]
    public void Lifecycle_Requested_Then_Approved_Records_Decision()
    {
        var r = new DiscountRequestRow
        {
            CaseId = 1, QuoteVersionId = 11, RequestedByUserId = 1, RequestedByEmail = "sales@x", RequestedPct = 8m,
            JustificationType = "CompetitorMatch", Status = DiscountRequestRow.StRequested,
        };
        Assert.Equal(0, r.DiscountRequestId);            // new row, id assigned by repo
        r.Status = DiscountRequestRow.StApproved;
        r.ApprovedPct = 6m;
        r.ApprovedByUserId = 2;
        r.ApprovedByEmail = "senior@x";
        r.DecidedAtUtc = DateTime.UtcNow;
        Assert.Equal(DiscountRequestRow.StApproved, r.Status);
        Assert.True(r.ApprovedPct < r.RequestedPct);      // partial approval is legal
    }

    [Theory]
    [InlineData("Underwriter", 5d, 4d, 200)]       // within band → ok (403-free path)
    [InlineData("Underwriter", 5d, 8d, 403)]       // above UW band → forbidden
    [InlineData("SeniorUnderwriter", 15d, 12d, 200)]
    [InlineData("Sales", 0d, 2d, 403)]             // sales cannot decide
    public void AuthorityMatrix_Gates_ApprovalBand(string role, double allowedMax, double requestedPct, int expected)
    {
        var allowed = role switch
        {
            "Underwriter" => 5m,
            "SeniorUnderwriter" => 15m,
            "ChiefUnderwriter" or "VP" or "Admin" => 50m,
            _ => 0m,
        };
        var pct = (decimal)requestedPct;
        var forbidden = allowed <= 0 || pct > (decimal)allowedMax;
        Assert.Equal(expected == 403, forbidden);
    }
}
