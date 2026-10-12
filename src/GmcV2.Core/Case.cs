namespace GmcV2.Core;

/// <summary>A GMC v2 group-mediclaim case underwriting record (core.Case row, in-memory).</summary>
public sealed class Case
{
    public long CaseId { get; set; }
    /// <summary>Format GMC-C-{FY}-{seq}, e.g. GMC-C-2627-00042.</summary>
    public string CaseNumber { get; set; } = "";
    public string BusinessType { get; set; } = Enums.BizRollover;
    public string BusinessSubType { get; set; } = "Main";
    public string ClientName { get; set; } = "";
    public string Status { get; set; } = Enums.CaseDraft;
    public string? CurrentStage { get; set; }
    public int CurrentStageAttempt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<CaseDocument> Documents { get; set; } = new();
    public List<CaseStageRun> StageRuns { get; set; } = new();
    public List<CaseException> Exceptions { get; set; } = new();
    public List<QuoteVersion> QuoteVersions { get; set; } = new();
}

public sealed class CaseDocument
{
    public long DocumentId { get; set; }
    public long CaseId { get; set; }
    public string DocType { get; set; } = "";   // ClaimsRegister | Enrollment | ExpiringPolicy | Rfq | CompetitorQuote
    public string FileName { get; set; } = "";
    public string StorageUri { get; set; } = "";
    public string ParseStatus { get; set; } = "Pending"; // Pending | Parsed | Unreadable
    public string Sha256 { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CaseStageRun
{
    public long StageRunId { get; set; }
    public long CaseId { get; set; }
    public string Stage { get; set; } = "";
    public int AttemptNo { get; set; } = 1;
    public string State { get; set; } = Enums.StagePending;
    public string? InputsHash { get; set; }
    public string? DetailJson { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public decimal? ElapsedSeconds { get; set; }
}

public sealed class CaseException
{
    public long ExceptionId { get; set; }
    public long CaseId { get; set; }
    public string Code { get; set; } = "";
    public bool Blocking { get; set; } = true;
    public string Status { get; set; } = "Open";   // Open | Resolved
    public string SubjectType { get; set; } = "";
    public string SubjectRef { get; set; } = "";
    public string Message { get; set; } = "";
    public string? DetailJson { get; set; }
    public string ResolutionTarget { get; set; } = "";  // e.g. Mapping Workbench
    public string ResumeStage { get; set; } = "";
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public sealed class QuoteVersion
{
    public long QuoteVersionId { get; set; }
    public long CaseId { get; set; }
    public int VersionNo { get; set; } = 1;
    public string Status { get; set; } = Enums.QuoteCurrent;
    public string GenerationMode { get; set; } = "Automatic";
    public int? SupersedesQuoteVersionId { get; set; }
    public string? RevisionReason { get; set; }
    public string? RevisionNotes { get; set; }
    public decimal GrossPremium { get; set; }
    public decimal NetPremium { get; set; }
    public string? InputsHash { get; set; }
    public PricingResult? Pricing { get; set; }
    public AnalyticsSnapshot? Analytics { get; set; }
    public GuardrailResult? Guardrails { get; set; }
    public List<BenefitDeltaLine> BenefitDelta { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "Pipeline";
}

/// <summary>Append-only decision log row (uw.UnderwritingAction).</summary>
public sealed class UnderwritingAction
{
    public long ActionId { get; set; }
    public long CaseId { get; set; }
    public int QuoteVersionId { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? Remarks { get; set; }
    public string AuthorityCheck { get; set; } = "Pass";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
