namespace GmcV2.Core;

/// <summary>Normative enumerations from technical_code_and_pages.md §3. Single source of truth.</summary>
public static class Enums
{
    // ─── Case statuses (workflow §4.1) ───
    public const string CaseDraft = "Draft";
    public const string CaseAwaitingDocuments = "AwaitingDocuments";
    public const string CaseProcessing = "Processing";
    public const string CaseExceptionPending = "ExceptionPending";
    public const string CaseAutoQuoted = "AutoQuoted";
    public const string CaseReferredToUW = "ReferredToUW";
    public const string CaseUnderReview = "UnderReview";
    public const string CaseDiscountRequested = "DiscountRequested";
    public const string CaseApproved = "Approved";
    public const string CaseApprovedWithChanges = "ApprovedWithChanges";
    public const string CaseReturnedForClarification = "ReturnedForClarification";
    public const string CaseDeclined = "Declined";
    public const string CaseCancelled = "Cancelled";
    public const string CaseExpired = "Expired";

    // ─── Quote version statuses (workflow §4.2) ───
    public const string QuoteDraft = "Draft";
    public const string QuoteCurrent = "Current";
    public const string QuoteSuperseded = "Superseded";
    public const string QuoteWithdrawn = "Withdrawn";

    // ─── Stage states (workflow §4.3) ───
    public const string StagePending = "Pending";
    public const string StageRunning = "Running";
    public const string StageCompleted = "Completed";
    public const string StageNeedsReview = "NeedsReview";
    public const string StageFailed = "Failed";
    public const string StageSkipped = "Skipped";

    // ─── Thirteen pipeline stages, in order (workflow §5, tech §3.4) ───
    public static readonly string[] Stages =
    {
        "Intake", "Profile", "ExtractDocuments", "Map", "Standardize", "CodeClinical",
        "Analyze", "Guardrail", "Price", "CostBenefits", "CompareCompetition",
        "AssembleQuote", "Publish"
    };

    // ─── Exception codes (tech §3.5) ───
    public const string ExcDocUnreadable = "EXC-DOC-UNREADABLE";
    public const string ExcDocFieldLow = "EXC-DOC-FIELD-LOW";
    public const string ExcMapLowConf = "EXC-MAP-LOW-CONF";
    public const string ExcMapRequiredMissing = "EXC-MAP-REQUIRED-MISSING";
    public const string ExcMapDuplicateTarget = "EXC-MAP-DUPLICATE-TARGET";
    public const string ExcStdDqLow = "EXC-STD-DQ-LOW";
    public const string ExcClinUnmapped = "EXC-CLIN-UNMAPPED";
    public const string ExcClinReview = "EXC-CLIN-REVIEW";
    public const string ExcGrdFailed = "EXC-GRD-FAILED";
    public const string ExcPrcMasterMissing = "EXC-PRC-MASTER-MISSING";
    public const string ExcPrcInvalid = "EXC-PRC-INVALID";
    public const string ExcAiUnavailable = "EXC-AI-UNAVAILABLE";
    public const string ExcAuthLimit = "EXC-AUTH-LIMIT";

    /// <summary>All 13 codes. Blocking codes park the case; advisory travel with the quote.</summary>
    public static readonly (string Code, bool Blocking)[] ExceptionCatalogue =
    {
        (ExcDocUnreadable, true), (ExcDocFieldLow, true), (ExcMapLowConf, true),
        (ExcMapRequiredMissing, true), (ExcMapDuplicateTarget, true), (ExcStdDqLow, true),
        (ExcClinUnmapped, true), (ExcClinReview, true), (ExcGrdFailed, true),
        (ExcPrcMasterMissing, true), (ExcPrcInvalid, true),
        (ExcAiUnavailable, true), (ExcAuthLimit, false),
    };

    // ─── Decision actions (tech §3.6) ───
    public const string ActApprove = "Approve";
    public const string ActApproveWithChanges = "ApproveWithChanges";
    public const string ActManualQuote = "ManualQuote";
    public const string ActReturnForClarification = "ReturnForClarification";
    public const string ActNeedMoreInfo = "NeedMoreInfo";
    public const string ActDecline = "Decline";

    // ─── Revision reasons (tech §3.7) ───
    public const string RevChronicRiskLoading = "ChronicRiskLoading";
    public const string RevLowClaimRatioDiscount = "LowClaimRatioDiscount";
    public const string RevBenefitChange = "BenefitChange";
    public const string RevCommercialDiscount = "CommercialDiscount";
    public const string RevDataCorrection = "DataCorrection";
    public const string RevCompetitionResponse = "CompetitionResponse";
    public const string RevAuthorityDirection = "AuthorityDirection";
    public const string RevOther = "Other";
    public static readonly string[] RevisionReasons =
        { RevChronicRiskLoading, RevLowClaimRatioDiscount, RevBenefitChange, RevCommercialDiscount,
          RevDataCorrection, RevCompetitionResponse, RevAuthorityDirection, RevOther };

    // ─── Mapping provenance tiers, precedence order (tech §3.8) ───
    public const string TierExactDictionary = "ExactDictionary";
    public const string TierLearnedAlias = "LearnedAlias";
    public const string TierMaster = "Master";          // clinical coding tier — disease master list
    public const string TierGolden = "Golden";          // clinical coding tier — human-confirmed golden dataset
    public const string TierRetrieval = "Retrieval";
    public const string TierLlmSemantic = "LlmSemantic";
    public const string TierFuzzy = "Fuzzy";

    // ─── Pricing component sources (workflow §9) ───
    public const string SrcMaster = "Master";
    public const string SrcOverride = "Override";
    public const string SrcComputed = "Computed";
    public const string SrcBenefitDelta = "BenefitDelta";
    public const string SrcDiscount = "Discount";
    public const string SrcTax = "Tax";

    // ─── Business types (workflow §10) ───
    public const string BizRollover = "Rollover";
    public const string BizFresh = "Fresh";
    public const string BizRenewalInternal = "RenewalInternal";
    public const string BizRenewalExternal = "RenewalExternal";

    /// <summary>Required documents per business type (workflow Stage 0 checklist).</summary>
    public static readonly Dictionary<string, string[]> RequiredDocuments = new()
    {
        [BizRollover] = new[] { "ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq" },
        [BizFresh] = new[] { "Enrollment", "Rfq" },
        [BizRenewalInternal] = new[] { "ClaimsRegister", "Enrollment" },
        [BizRenewalExternal] = new[] { "ClaimsRegister", "Enrollment", "ExpiringPolicy", "Rfq" },
    };

    // ─── Roles (workflow §3) ───
    public const string RoleSalesAgent = "SalesAgent";
    public const string RoleUnderwriter = "Underwriter";
    public const string RoleSeniorUW = "SeniorUnderwriter";
    public const string RoleVP = "VicePresident";
    public const string RoleAdmin = "Admin";
    public static readonly string[] Roles = { RoleSalesAgent, RoleUnderwriter, RoleSeniorUW, RoleVP, RoleAdmin };
}
