namespace GmcV2.Core;

/// <summary>One current coding per standardized claim (ing.ClaimCoding, db §6.1). Versioned: a correction supersedes the current row.</summary>
public sealed class ClaimCoding
{
    public long ClaimCodingId { get; set; }
    public long StdClaimId { get; set; }
    public long CaseId { get; set; }
    public int CodingVersion { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;

    public string? Level1IcdCode { get; set; }
    public string? Level1DiseaseName { get; set; }
    public decimal? Level1Confidence { get; set; }
    public string? Level1Provenance { get; set; }   // Master, Rule, RAG, LLM, Manual, Source

    public string? Level2IcdCode { get; set; }
    public string? Level2DiseaseName { get; set; }
    public decimal? Level2Confidence { get; set; }
    public string? Level2Provenance { get; set; }

    public string? DiseaseGroup { get; set; }
    public string? Department { get; set; }
    public bool? IsChronic { get; set; }
    public string? BenefitGroup { get; set; }        // Cataract, Hernia, KidneyUrology, JointOrthopedic, Maternity, Other
    public string? MaternityDeliveryType { get; set; } // Normal, CSection, null
    public string? ReasonText { get; set; }

    public bool NeedsHumanReview { get; set; }
    /// <summary>AutoAccepted | InReview | HumanAccepted | HumanCorrected | Unmapped | Uncertain.</summary>
    public string CodingStatus { get; set; } = "AutoAccepted";
    public long? GoldenEntryId { get; set; }

    /// <summary>App-level denormalization for the review workbench join (not a SQL column).</summary>
    public string? ClaimNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Human review audit — the bridge between the review queue and the golden dataset (ing.CodingReview, db §6.2).</summary>
public sealed class CodingReview
{
    public long CodingReviewId { get; set; }
    public long ClaimCodingId { get; set; }
    public string Reviewer { get; set; } = "System";
    /// <summary>Accepted | Corrected | Rejected | MarkedUncertain | NeedMoreInfo.</summary>
    public string Decision { get; set; } = "Accepted";
    public string? SuggestedLevel1Json { get; set; }
    public string? SuggestedLevel2Json { get; set; }
    public string? FinalLevel1Json { get; set; }
    public string? FinalLevel2Json { get; set; }
    public string? Comments { get; set; }
    public bool PromotedToGolden { get; set; }
    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
}
