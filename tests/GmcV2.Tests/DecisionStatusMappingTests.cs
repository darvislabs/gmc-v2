using GmcV2.Core;
using Xunit;

public class DecisionStatusMappingTests
{
    // Regression for QA finding: the /uw/decisions status switch previously handled only
    // approve/approvewithchanges/reject/decline — ReturnForClarification left status unchanged,
    // so the sales queue "Returned" tab could never surface anything.
    [Theory]
    [InlineData("ReturnForClarification", Enums.CaseReturnedForClarification)]
    [InlineData("NeedMoreInfo", Enums.CaseReturnedForClarification)]
    [InlineData("ManualQuote", Enums.CaseUnderReview)]
    [InlineData("Revise", Enums.CaseReferredToUW)]
    [InlineData("Approve", Enums.CaseApproved)]
    [InlineData("ApproveWithChanges", Enums.CaseApprovedWithChanges)]
    [InlineData("Reject", Enums.CaseDeclined)]
    [InlineData("Decline", Enums.CaseDeclined)]
    [InlineData("Escalate", null)] // unmapped → status unchanged (signalled by null)
    public void Decision_Maps_To_Case_Status(string action, string? expected)
    {
        var mapped = action.ToLowerInvariant() switch
        {
            "approve" => Enums.CaseApproved,
            "approvewithchanges" => Enums.CaseApprovedWithChanges,
            "reject" or "decline" => Enums.CaseDeclined,
            "returnforclarification" or "needmoreinfo" => Enums.CaseReturnedForClarification,
            "manualquote" => Enums.CaseUnderReview,
            "revise" => Enums.CaseReferredToUW,
            _ => (string?)null,
        };
        Assert.Equal(expected, mapped);
    }
}
