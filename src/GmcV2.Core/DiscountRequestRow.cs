using GmcV2.Core;

namespace GmcV2.Core;

/// <summary>Commercial discount request (prototype v74 step 8, Sales↔UW loop). Lifecycle:
/// Requested → Approved / Declined / Withdrawn. Approval authority from uw.AuthorityMatrix
/// (UW 5% / Senior 15% / Chief 50%). On approval the discount rides into the quote as an
/// override component; the resulting quote version is linked.</summary>
public sealed class DiscountRequestRow
{
    public const string StRequested = "Requested";
    public const string StApproved = "Approved";
    public const string StDeclined = "Declined";
    public const string StWithdrawn = "Withdrawn";

    public long DiscountRequestId { get; set; }
    public long CaseId { get; set; }
    public long QuoteVersionId { get; set; }
    public int RequestedByUserId { get; set; }
    public string RequestedByEmail { get; set; } = "";
    public decimal RequestedPct { get; set; }
    public string? RequestedRemarks { get; set; }
    public string? JustificationType { get; set; }   // CommercialDiscount | LowClaimRatioDiscount | Retention | CompetitorMatch
    public long? CompetitorQuoteId { get; set; }
    public string Status { get; set; } = StRequested;
    public decimal? ApprovedPct { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByEmail { get; set; }
    public string? ApprovalRemarks { get; set; }
    public long? ResultingQuoteVersionId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAtUtc { get; set; }
}
