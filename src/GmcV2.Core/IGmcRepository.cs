namespace GmcV2.Core;

/// <summary>
/// The repository boundary. GmcV2.Infrastructure provides the SQL Server (docker GMCv2)
/// implementation; engines and orchestrator depend only on this interface — so the whole
/// calculation core is unit-testable in memory.
/// </summary>
public interface IGmcRepository
{
    // Cases
    Task<Case> CreateCaseAsync(Case c);
    Task<Case?> GetCaseAsync(long caseId);
    Task<Case?> GetCaseByNumberAsync(string caseNumber);
    Task UpdateCaseAsync(Case c);
    Task<List<Case>> ListCasesAsync(string? status = null);

    // Documents
    Task<CaseDocument> AddDocumentAsync(CaseDocument d);
    Task<CaseDocument?> GetDocumentAsync(long documentId);
    Task UpdateDocumentAsync(CaseDocument d);

    // Stage runs
    Task<CaseStageRun> StartStageRunAsync(long caseId, string stage, int attemptNo, string inputsHash);
    Task<CaseStageRun?> GetLatestStageRunAsync(long caseId, string stage);
    Task CompleteStageRunAsync(long stageRunId, string state, string? detailJson, decimal elapsedSeconds);

    // Exceptions
    Task<CaseException> RaiseExceptionAsync(CaseException e);
    Task<List<CaseException>> GetOpenExceptionsAsync(long caseId, bool blockingOnly = false);
    Task<CaseException?> GetExceptionAsync(long exceptionId);
    Task ResolveExceptionAsync(long exceptionId, string resolvedBy);

    // Notes
    Task AddNoteAsync(long caseId, string author, string text);

    // Quote versions
    Task<QuoteVersion> AddQuoteVersionAsync(QuoteVersion q);
    Task AddQuoteArtifactAsync(long quoteVersionId, string artifactType, string fileName, byte[] content, string generatedBy);
    Task<List<QuoteArtifactInfo>> GetQuoteArtifactsAsync(long quoteVersionId);
    Task<(string Type, string Content)?> GetQuoteArtifactContentAsync(long artifactId);
    Task<List<QuoteVersion>> GetQuoteVersionsAsync(long caseId);
    Task<QuoteVersion?> GetCurrentQuoteVersionAsync(long caseId);
    Task SupersedeQuoteVersionAsync(long caseId, int newVersionId);

    // Underwriting actions
    Task AddUnderwritingActionAsync(UnderwritingAction a);

    // Ingestion
    Task<List<RawRow>> AddRawRowsAsync(long documentId, string sheetName, List<string[]> rows);
    Task<List<RawRow>> GetRawRowsAsync(long documentId);
    Task<SourceColumn> AddOrUpdateSourceColumnAsync(SourceColumn col);
    Task<List<SourceColumn>> GetSourceColumnsAsync(long documentId);
    Task AddColumnMappingAsync(ColumnMapping m);
    Task<ColumnMapping?> GetLatestMappingAsync(long sourceColumnId);
    Task LearnSynonymAsync(ColumnSynonym syn);

    // Knowledge
    Task<List<DiseaseEntry>> GetDiseasesAsync();
    Task AddDiseaseAsync(DiseaseEntry d);
    Task<List<GoldenEntry>> GetGoldenEntriesAsync();
    Task AddGoldenEntryAsync(GoldenEntry e);
    Task IncrementGoldenUseAsync(long goldenEntryId);

    // Pricing masters
    Task<PricingMasterVersion> GetActiveMasterVersionAsync();
    Task<List<PricingMasterVersion>> ListMasterVersionsAsync();
    Task AddMasterVersionAsync(PricingMasterVersion v);
    Task UpdateMasterVersionAsync(PricingMasterVersion v);

    // Guardrails
    Task<List<GuardrailRuleDef>> GetGuardrailRulesAsync();
    Task UpsertGuardrailRuleAsync(GuardrailRuleDef r);

    // Reference
    Task<Dictionary<string, string>> GetPaidStatusMapAsync();

    // ── users + notifications (workflow §3, §12) ──
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserAsync(long userId);
    Task<User> AddUserAsync(User u);
    Task UpdateUserAsync(User u);
    Task<List<User>> ListUsersAsync();
    Task UpdateLastLoginAsync(long userId);
    Task AddNotificationAsync(Notification n);
    Task<List<Notification>> GetNotificationsAsync(string? role, long? userId, bool unreadOnly = false);
    Task MarkNotificationReadAsync(long notificationId);
    Task<int> MarkAllNotificationsReadAsync(string? role, long? userId);
}
