using System.Collections.Concurrent;
using System.Text;
using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Complete in-memory IGmcRepository for dev/CI. Thread-safe via a single
/// lock; case numbers via Interlocked-style counters. Activated when
/// GMCV2_DBMODE=memory so the platform runs WITHOUT SQL.
/// </summary>
public sealed class InMemoryRepository : IGmcRepository
{
    private readonly InMemoryUserStore _userStore = new();

    public Task<User?> GetUserByEmailAsync(string email) => _userStore.GetUserByEmailAsync(email);
    public Task<User?> GetUserAsync(long userId) => _userStore.GetUserAsync(userId);
    public Task<User> AddUserAsync(User u) => _userStore.AddUserAsync(u);
    public Task UpdateUserAsync(User u) => _userStore.UpdateUserAsync(u);
    public Task<List<User>> ListUsersAsync() => _userStore.ListUsersAsync();
    public Task UpdateLastLoginAsync(long userId) => _userStore.UpdateLastLoginAsync(userId);
    public Task AddNotificationAsync(Notification n) => _userStore.AddNotificationAsync(n);
    public Task<List<Notification>> GetNotificationsAsync(string? role, long? userId, bool unreadOnly = false) => _userStore.GetNotificationsAsync(role, userId, unreadOnly);
    public Task MarkNotificationReadAsync(long notificationId) => _userStore.MarkNotificationReadAsync(notificationId);
    public Task<int> MarkAllNotificationsReadAsync(string? role, long? userId) => _userStore.MarkAllNotificationsReadAsync(role, userId);

    private readonly object _gate = new();

    private long _caseSeq;
    private long _docSeq;
    private long _stageRunSeq;
    private long _exceptionSeq;
    private long _quoteSeq;
    private long _rawRowSeq;
    private long _sourceColumnSeq;
    private long _mappingSeq;
    private long _synonymSeq;
    private long _diseaseSeq;
    private long _goldenSeq;
    private int _masterSeq;

    private readonly Dictionary<long, Case> _cases = new();
    private readonly Dictionary<long, CaseDocument> _documents = new();
    private readonly Dictionary<long, CaseStageRun> _stageRuns = new();
    private readonly Dictionary<long, CaseException> _exceptions = new();
    private readonly List<(long CaseId, string Author, string Text, DateTime AtUtc)> _notes = new();
    private readonly Dictionary<long, QuoteVersion> _quotes = new();
    private readonly List<UnderwritingAction> _actions = new();
    private readonly Dictionary<long, List<RawRow>> _rawRows = new();
    private readonly Dictionary<long, SourceColumn> _sourceColumns = new();
    private readonly List<ColumnMapping> _mappings = new();
    private readonly List<ColumnSynonym> _synonyms = new();
    private readonly List<DiseaseEntry> _diseases = new();
    private readonly List<GoldenEntry> _golden = new();
    private readonly List<PricingMasterVersion> _masters = new();
    private readonly List<GuardrailRuleDef> _guardrailRules = new();
    private readonly Dictionary<string, string> _paidStatus = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seed hook for SeedRunner (reference data has no IGmcRepository writer).</summary>
    internal void SeedPaidStatus((string Raw, string Normalized)[] pairs)
    {
        lock (_gate)
            foreach (var (raw, norm) in pairs)
                if (!_paidStatus.ContainsKey(raw))
                    _paidStatus[raw] = norm;
    }

    // per-FY counters for case numbers
    private readonly Dictionary<string, long> _caseNumberSeq = new();
    private readonly Dictionary<string, long> _quoteNumberSeq = new();

    private static string J<T>(T v) => System.Text.Json.JsonSerializer.Serialize(v);

    // ─────────────────────────── cases ───────────────────────────

    public Task<Case> CreateCaseAsync(Case c)
    {
        lock (_gate)
        {
            var now = c.CreatedAtUtc == default ? DateTime.UtcNow : c.CreatedAtUtc;
            // Indian FY April–March in yy-yy form.
            var fyStartYear = now.Month >= 4 ? now.Year : now.Year - 1;
            var fy = $"{fyStartYear % 100:00}{(fyStartYear + 1) % 100:00}";
            var seq = _caseNumberSeq.TryGetValue(fy, out var s) ? s + 1 : 1;
            _caseNumberSeq[fy] = seq;
            c.CaseNumber = $"GMC-C-{fy}-{seq:D5}";
            c.CaseId = ++_caseSeq;
            c.CreatedAtUtc = now;
            c.UpdatedAtUtc = now;
            _cases[c.CaseId] = c;
            return Task.FromResult(c);
        }
    }

    public Task<Case?> GetCaseAsync(long caseId)
    {
        lock (_gate)
        {
            if (!_cases.TryGetValue(caseId, out var c)) return Task.FromResult<Case?>(null);
            return Task.FromResult<Case?>(CloneCase(c));
        }
    }

    public Task<Case?> GetCaseByNumberAsync(string caseNumber)
    {
        lock (_gate)
        {
            var c = _cases.Values.FirstOrDefault(x => x.CaseNumber == caseNumber);
            return Task.FromResult(c is null ? null : CloneCase(c));
        }
    }

    public Task UpdateCaseAsync(Case c)
    {
        lock (_gate)
        {
            if (_cases.TryGetValue(c.CaseId, out var stored))
            {
                stored.ClientName = c.ClientName;
                stored.BusinessType = c.BusinessType;
                stored.BusinessSubType = c.BusinessSubType;
                stored.Status = c.Status;
                stored.CurrentStage = c.CurrentStage;
                stored.CurrentStageAttempt = c.CurrentStageAttempt;
                stored.UpdatedAtUtc = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }
    }

    public Task<List<Case>> ListCasesAsync(string? status = null)
    {
        lock (_gate)
        {
            var list = _cases.Values
                .Where(c => status is null || c.Status == status)
                .OrderByDescending(c => c.CaseId)
                .Select(CloneCase)
                .ToList();
            return Task.FromResult(list);
        }
    }

    private static Case CloneCase(Case c) => new()
    {
        CaseId = c.CaseId,
        CaseNumber = c.CaseNumber,
        BusinessType = c.BusinessType,
        BusinessSubType = c.BusinessSubType,
        ClientName = c.ClientName,
        Status = c.Status,
        CurrentStage = c.CurrentStage,
        CurrentStageAttempt = c.CurrentStageAttempt,
        CreatedBy = c.CreatedBy,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc,
        Documents = c.Documents.Select(d => new CaseDocument
        {
            DocumentId = d.DocumentId, CaseId = d.CaseId, DocType = d.DocType, FileName = d.FileName,
            StorageUri = d.StorageUri, ParseStatus = d.ParseStatus, Sha256 = d.Sha256,
            UploadedAtUtc = d.UploadedAtUtc,
        }).ToList(),
        StageRuns = c.StageRuns.Select(r => new CaseStageRun
        {
            StageRunId = r.StageRunId, CaseId = r.CaseId, Stage = r.Stage, AttemptNo = r.AttemptNo,
            State = r.State, InputsHash = r.InputsHash, DetailJson = r.DetailJson,
            StartedAtUtc = r.StartedAtUtc, CompletedAtUtc = r.CompletedAtUtc, ElapsedSeconds = r.ElapsedSeconds,
        }).ToList(),
        Exceptions = c.Exceptions.Select(e => new CaseException
        {
            ExceptionId = e.ExceptionId, CaseId = e.CaseId, Code = e.Code, Blocking = e.Blocking,
            Status = e.Status, SubjectType = e.SubjectType, SubjectRef = e.SubjectRef, Message = e.Message,
            DetailJson = e.DetailJson, ResolutionTarget = e.ResolutionTarget, ResumeStage = e.ResumeStage,
            ResolvedBy = e.ResolvedBy, ResolvedAtUtc = e.ResolvedAtUtc,
        }).ToList(),
        QuoteVersions = c.QuoteVersions.Select(CloneQuote).ToList(),
    };

    private static QuoteVersion CloneQuote(QuoteVersion q) => new()
    {
        QuoteVersionId = q.QuoteVersionId, CaseId = q.CaseId, VersionNo = q.VersionNo, Status = q.Status,
        GenerationMode = q.GenerationMode, SupersedesQuoteVersionId = q.SupersedesQuoteVersionId,
        RevisionReason = q.RevisionReason, RevisionNotes = q.RevisionNotes,
        GrossPremium = q.GrossPremium, NetPremium = q.NetPremium, InputsHash = q.InputsHash,
        Pricing = q.Pricing, Analytics = q.Analytics, Guardrails = q.Guardrails,
        BenefitDelta = q.BenefitDelta.ToList(), CreatedAtUtc = q.CreatedAtUtc, CreatedBy = q.CreatedBy,
    };

    private static CaseException CloneException(CaseException e) => new()
    {
        ExceptionId = e.ExceptionId, CaseId = e.CaseId, Code = e.Code, Blocking = e.Blocking,
        Status = e.Status, SubjectType = e.SubjectType, SubjectRef = e.SubjectRef, Message = e.Message,
        DetailJson = e.DetailJson, ResolutionTarget = e.ResolutionTarget, ResumeStage = e.ResumeStage,
        ResolvedBy = e.ResolvedBy, ResolvedAtUtc = e.ResolvedAtUtc,
    };

    // ─────────────────────────── documents ───────────────────────────

    public Task<CaseDocument> AddDocumentAsync(CaseDocument d)
    {
        lock (_gate)
        {
            d.DocumentId = ++_docSeq;
            d.UploadedAtUtc = DateTime.UtcNow;
            _documents[d.DocumentId] = d;
            if (_cases.TryGetValue(d.CaseId, out var c) && !c.Documents.Any(x => x.DocumentId == d.DocumentId))
                c.Documents.Add(d);
            return Task.FromResult(d);
        }
    }

    public Task<CaseDocument?> GetDocumentAsync(long documentId)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(documentId, out var d) ? d : null);
        }
    }

    public Task UpdateDocumentAsync(CaseDocument d)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(d.DocumentId, out var stored))
            {
                stored.ParseStatus = d.ParseStatus;
                stored.Sha256 = d.Sha256;
                stored.FileName = d.FileName;
                stored.StorageUri = d.StorageUri;
            }
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── stage runs ───────────────────────────

    public Task<CaseStageRun> StartStageRunAsync(long caseId, string stage, int attemptNo, string inputsHash)
    {
        lock (_gate)
        {
            var run = new CaseStageRun
            {
                StageRunId = ++_stageRunSeq,
                CaseId = caseId,
                Stage = stage,
                AttemptNo = attemptNo,
                State = Enums.StageRunning,
                InputsHash = inputsHash,
                StartedAtUtc = DateTime.UtcNow,
            };
            _stageRuns[run.StageRunId] = run;
            if (_cases.TryGetValue(caseId, out var c)) c.StageRuns.Add(run);
            return Task.FromResult(run);
        }
    }

    public Task<CaseStageRun?> GetLatestStageRunAsync(long caseId, string stage)
    {
        lock (_gate)
        {
            var run = _stageRuns.Values
                .Where(r => r.CaseId == caseId && r.Stage == stage)
                .OrderByDescending(r => r.AttemptNo)
                .ThenByDescending(r => r.StageRunId)
                .FirstOrDefault();
            return Task.FromResult(run);
        }
    }

    public Task CompleteStageRunAsync(long stageRunId, string state, string? detailJson, decimal elapsedSeconds)
    {
        lock (_gate)
        {
            if (_stageRuns.TryGetValue(stageRunId, out var run))
            {
                run.State = state;
                run.DetailJson = detailJson;
                run.CompletedAtUtc = DateTime.UtcNow;
                run.ElapsedSeconds = elapsedSeconds;
            }
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── exceptions ───────────────────────────

    public Task<CaseException> RaiseExceptionAsync(CaseException e)
    {
        lock (_gate)
        {
            // Dedup: one open exception per (case, code, subject) — a resume that
            // re-raises the same problem updates the message/timestamp instead of stacking rows.
            var existing = _exceptions.Values.FirstOrDefault(x =>
                x.CaseId == e.CaseId && x.Code == e.Code
                && x.SubjectRef == e.SubjectRef && x.Status == "Open");
            if (existing is not null)
            {
                existing.Message = e.Message;
                existing.ResumeStage = string.IsNullOrEmpty(e.ResumeStage) ? existing.ResumeStage : e.ResumeStage;
                return Task.FromResult(CloneException(existing));
            }
            e.ExceptionId = ++_exceptionSeq;
            e.Status = string.IsNullOrEmpty(e.Status) ? "Open" : e.Status;
            _exceptions[e.ExceptionId] = e;
            return Task.FromResult(CloneException(e));
        }
    }

    public Task<List<CaseException>> GetOpenExceptionsAsync(long caseId, bool blockingOnly = false)
    {
        lock (_gate)
        {
            var list = _exceptions.Values
                .Where(e => e.CaseId == caseId && e.Status is "Open" or "Assigned" && (!blockingOnly || e.Blocking))
                .OrderBy(e => e.ExceptionId)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<CaseException?> GetExceptionAsync(long exceptionId)
    {
        lock (_gate)
        {
            return Task.FromResult(_exceptions.TryGetValue(exceptionId, out var e) ? e : null);
        }
    }

    public Task ResolveExceptionAsync(long exceptionId, string resolvedBy)
    {
        lock (_gate)
        {
            if (_exceptions.TryGetValue(exceptionId, out var e))
            {
                e.Status = "Resolved";
                e.ResolvedBy = resolvedBy;
                e.ResolvedAtUtc = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── notes ───────────────────────────

    public Task AddNoteAsync(long caseId, string author, string text)
    {
        lock (_gate)
        {
            _notes.Add((caseId, author, text, DateTime.UtcNow));
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── quote versions ───────────────────────────

    private readonly List<(long QuoteVersionId, string ArtifactType, string FileName, byte[] Content, string GeneratedBy, DateTime AtUtc)> _artifacts = new();
    private long _artifactSeq = 0;

    public Task AddQuoteArtifactAsync(long quoteVersionId, string artifactType, string fileName, byte[] content, string generatedBy)
    {
        lock (_gate)
        {
            _artifacts.Add((quoteVersionId, artifactType, fileName, content, generatedBy, DateTime.UtcNow));
            _artifactSeq++;
            return Task.CompletedTask;
        }
    }

    public Task<List<QuoteArtifactInfo>> GetQuoteArtifactsAsync(long quoteVersionId)
    {
        lock (_gate)
        {
            var list = _artifacts
                .Select((a, i) => (a, Id: i + 1))
                .Where(x => x.a.QuoteVersionId == quoteVersionId)
                .Select(x => new QuoteArtifactInfo
                {
                    QuoteArtifactId = x.Id,
                    QuoteVersionId = x.a.QuoteVersionId,
                    ArtifactType = x.a.ArtifactType,
                    FileName = x.a.FileName,
                    SizeBytes = x.a.Content.LongLength,
                    GeneratedAtUtc = x.a.AtUtc,
                    GeneratedBy = x.a.GeneratedBy,
                })
                .ToList<QuoteArtifactInfo>();
            return Task.FromResult(list);
        }
    }

    public Task<(string Type, string Content)?> GetQuoteArtifactContentAsync(long artifactId)
    {
        lock (_gate)
        {
            if (artifactId < 1 || artifactId > _artifacts.Count) return Task.FromResult<(string, string)?>(null);
            var a = _artifacts[(int)artifactId - 1];
            return Task.FromResult<(string, string)?>((a.ArtifactType, Encoding.UTF8.GetString(a.Content)));
        }
    }

    public Task<QuoteVersion> AddQuoteVersionAsync(QuoteVersion q)
    {
        lock (_gate)
        {
            var caseNo = _cases.TryGetValue(q.CaseId, out var c) ? c.CaseNumber : "";
            var fyPart = caseNo.StartsWith("GMC-C-", StringComparison.Ordinal) && caseNo.Length >= 10
                ? caseNo.Substring(6, 4) : "0000";
            var seq = _quoteNumberSeq.TryGetValue(fyPart, out var s) ? s + 1 : 1;
            _quoteNumberSeq[fyPart] = seq;
            q.QuoteVersionId = ++_quoteSeq;
            q.CreatedAtUtc = DateTime.UtcNow;

            // Exactly one Current per case.
            if (q.Status == Enums.QuoteCurrent)
                foreach (var other in _quotes.Values.Where(x => x.CaseId == q.CaseId && x.Status == Enums.QuoteCurrent))
                    other.Status = Enums.QuoteSuperseded;

            _quotes[q.QuoteVersionId] = q;
            if (_cases.TryGetValue(q.CaseId, out var cs)) cs.QuoteVersions.Add(q);
            return Task.FromResult(q);
        }
    }

    public Task<List<QuoteVersion>> GetQuoteVersionsAsync(long caseId)
    {
        lock (_gate)
        {
            return Task.FromResult(_quotes.Values
                .Where(q => q.CaseId == caseId)
                .OrderBy(q => q.VersionNo)
                .Select(CloneQuote)
                .ToList());
        }
    }

    public Task<QuoteVersion?> GetCurrentQuoteVersionAsync(long caseId)
    {
        lock (_gate)
        {
            var q = _quotes.Values.FirstOrDefault(x => x.CaseId == caseId && x.Status == Enums.QuoteCurrent);
            return Task.FromResult(q is null ? null : CloneQuote(q));
        }
    }

    public Task SupersedeQuoteVersionAsync(long caseId, int newVersionId)
    {
        lock (_gate)
        {
            foreach (var q in _quotes.Values.Where(x => x.CaseId == caseId && x.Status == Enums.QuoteCurrent && x.QuoteVersionId != newVersionId))
                q.Status = Enums.QuoteSuperseded;
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── underwriting actions ───────────────────────────

    public Task AddUnderwritingActionAsync(UnderwritingAction a)
    {
        lock (_gate)
        {
            a.ActionId = _actions.Count + 1;
            _actions.Add(a);
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── ingestion ───────────────────────────

    public Task DeleteRawRowsAsync(long documentId, string sheetName)
    {
        lock (_gate)
        {
            if (_rawRows.TryGetValue(documentId, out var list))
                list.RemoveAll(r => (r.SheetName ?? "") == sheetName);
        }
        return Task.CompletedTask;
    }

    public Task<List<RawRow>> AddRawRowsAsync(long documentId, string sheetName, List<string[]> rows)
    {
        lock (_gate)
        {
            var list = _rawRows.TryGetValue(documentId, out var existing) ? existing : new List<RawRow>();
            _rawRows[documentId] = list;
            var start = list.Count;
            var result = new List<RawRow>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var payload = J(rows[i]);
                var row = new RawRow
                {
                    RawRowId = ++_rawRowSeq,
                    DocumentId = documentId,
                    RowNumber = start + i + 1,
                    SheetName = sheetName,
                    Cells = rows[i],
                    RowHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))),
                };
                list.Add(row);
                result.Add(row);
            }
            return Task.FromResult(result);
        }
    }

    public Task<List<RawRow>> GetRawRowsAsync(long documentId)
    {
        lock (_gate)
        {
            return Task.FromResult(_rawRows.TryGetValue(documentId, out var list)
                ? list.Select(r => new RawRow
                {
                    RawRowId = r.RawRowId, DocumentId = r.DocumentId, RowNumber = r.RowNumber,
                    SheetName = r.SheetName, Cells = r.Cells, RowHash = r.RowHash,
                }).ToList()
                : new List<RawRow>());
        }
    }

    public Task<SourceColumn> AddOrUpdateSourceColumnAsync(SourceColumn col)
    {
        lock (_gate)
        {
            if (col.SourceColumnId == 0)
            {
                col.SourceColumnId = ++_sourceColumnSeq;
                _sourceColumns[col.SourceColumnId] = col;
            }
            else
            {
                _sourceColumns[col.SourceColumnId] = col;
            }
            return Task.FromResult(col);
        }
    }

    public Task<List<SourceColumn>> GetSourceColumnsAsync(long documentId)
    {
        lock (_gate)
        {
            return Task.FromResult(_sourceColumns.Values
                .Where(sc => sc.DocumentId == documentId)
                .OrderBy(sc => sc.ColumnIndex)
                .ToList());
        }
    }

    public Task AddColumnMappingAsync(ColumnMapping m)
    {
        lock (_gate)
        {
            m.MappingId = ++_mappingSeq;
            _mappings.Add(m);
            return Task.CompletedTask;
        }
    }

    public Task<ColumnMapping?> GetLatestMappingAsync(long sourceColumnId)
    {
        lock (_gate)
        {
            return Task.FromResult(_mappings
                .Where(m => m.SourceColumnId == sourceColumnId)
                .OrderByDescending(m => m.MappingId)
                .FirstOrDefault());
        }
    }

    public Task LearnSynonymAsync(ColumnSynonym syn)
    {
        lock (_gate)
        {
            var existing = _synonyms.FirstOrDefault(s =>
                s.SynonymText.Equals(syn.SynonymText, StringComparison.OrdinalIgnoreCase)
                && s.TargetParameter == syn.TargetParameter);
            if (existing is not null)
            {
                existing.UseCount++;
                existing.LearnedFromApproval |= syn.LearnedFromApproval;
            }
            else
            {
                syn.SynonymId = ++_synonymSeq;
                _synonyms.Add(syn);
            }
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── clinical coding ───────────────────────────

    private readonly List<ClaimCoding> _coding = new();
    private readonly List<CodingReview> _codingReviews = new();
    private long _codingSeq;
    private long _codingReviewSeq;

    public Task<List<ClaimCoding>> GetClaimCodingAsync(long caseId, string? codingStatus = null)
    {
        lock (_gate) return Task.FromResult(_coding
            .Where(cc => cc.CaseId == caseId && cc.IsCurrent
                && (codingStatus is null || cc.CodingStatus == codingStatus))
            .OrderBy(cc => cc.ClaimCodingId)
            .Select(CloneCoding)
            .ToList());
    }

    public Task<ClaimCoding> SaveClaimCodingAsync(ClaimCoding coding)
    {
        lock (_gate)
        {
            // Corrections version the row: supersede the current one, insert the new version.
            var prev = _coding.LastOrDefault(x => x.StdClaimId == coding.StdClaimId && x.IsCurrent);
            if (prev is not null && prev.ClaimCodingId != coding.ClaimCodingId)
                prev.IsCurrent = false;
            if (coding.ClaimCodingId == 0)
            {
                coding.ClaimCodingId = ++_codingSeq;
                coding.CodingVersion = prev is null ? 1 : prev.CodingVersion + 1;
                coding.IsCurrent = true;
                _coding.Add(coding);
            }
            else
            {
                var idx = _coding.FindIndex(x => x.ClaimCodingId == coding.ClaimCodingId);
                if (idx < 0) throw new InvalidOperationException($"ClaimCoding {coding.ClaimCodingId} not found.");
                coding.UpdatedAtUtc = DateTime.UtcNow;
                _coding[idx] = coding;
            }
            return Task.FromResult(CloneCoding(coding));
        }
    }

    public Task AddCodingReviewAsync(CodingReview review)
    {
        lock (_gate)
        {
            review.CodingReviewId = ++_codingReviewSeq;
            review.ReviewedAtUtc = DateTime.UtcNow;
            _codingReviews.Add(review);
            return Task.CompletedTask;
        }
    }

    private static ClaimCoding CloneCoding(ClaimCoding c) => new()
    {
        ClaimCodingId = c.ClaimCodingId, StdClaimId = c.StdClaimId, CaseId = c.CaseId,
        CodingVersion = c.CodingVersion, IsCurrent = c.IsCurrent,
        Level1IcdCode = c.Level1IcdCode, Level1DiseaseName = c.Level1DiseaseName,
        Level1Confidence = c.Level1Confidence, Level1Provenance = c.Level1Provenance,
        Level2IcdCode = c.Level2IcdCode, Level2DiseaseName = c.Level2DiseaseName,
        Level2Confidence = c.Level2Confidence, Level2Provenance = c.Level2Provenance,
        DiseaseGroup = c.DiseaseGroup, Department = c.Department, IsChronic = c.IsChronic,
        BenefitGroup = c.BenefitGroup, MaternityDeliveryType = c.MaternityDeliveryType,
        ReasonText = c.ReasonText, NeedsHumanReview = c.NeedsHumanReview,
        CodingStatus = c.CodingStatus, GoldenEntryId = c.GoldenEntryId, ClaimNumber = c.ClaimNumber,
        CreatedAtUtc = c.CreatedAtUtc, UpdatedAtUtc = c.UpdatedAtUtc,
    };

    // ─────────────────────────── knowledge ───────────────────────────

    public Task<List<DiseaseEntry>> GetDiseasesAsync()
    {
        lock (_gate) return Task.FromResult(_diseases.Select(d => new DiseaseEntry
        {
            DiseaseId = d.DiseaseId, DiseaseName = d.DiseaseName, DiseaseGroup = d.DiseaseGroup,
            IcdLevel1 = d.IcdLevel1, IcdLevel2 = d.IcdLevel2,
            MatchKeywords = d.MatchKeywords.ToArray(), IsChronic = d.IsChronic,
        }).ToList());
    }

    public Task AddDiseaseAsync(DiseaseEntry d)
    {
        lock (_gate)
        {
            if (_diseases.Any(x => x.DiseaseName.Equals(d.DiseaseName, StringComparison.OrdinalIgnoreCase)
                                   && x.IcdLevel1 == d.IcdLevel1))
                return Task.CompletedTask;
            d.DiseaseId = ++_diseaseSeq;
            _diseases.Add(d);
            return Task.CompletedTask;
        }
    }

    public Task<List<GoldenEntry>> GetGoldenEntriesAsync()
    {
        lock (_gate) return Task.FromResult(_golden.Select(g => new GoldenEntry
        {
            GoldenEntryId = g.GoldenEntryId, GoldenVersion = g.GoldenVersion, RawTextNorm = g.RawTextNorm,
            IcdLevel1 = g.IcdLevel1, IcdLevel2 = g.IcdLevel2, UseCount = g.UseCount,
            WasCorrected = g.WasCorrected, Source = g.Source,
        }).ToList());
    }

    public Task AddGoldenEntryAsync(GoldenEntry e)
    {
        lock (_gate)
        {
            if (_golden.Any(x => x.RawTextNorm.Equals(e.RawTextNorm, StringComparison.OrdinalIgnoreCase)))
                return Task.CompletedTask;
            e.GoldenEntryId = ++_goldenSeq;
            _golden.Add(e);
            return Task.CompletedTask;
        }
    }

    public Task IncrementGoldenUseAsync(long goldenEntryId)
    {
        lock (_gate)
        {
            var g = _golden.FirstOrDefault(x => x.GoldenEntryId == goldenEntryId);
            if (g is not null) g.UseCount++;
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── pricing masters ───────────────────────────

    public Task<List<PricingMasterVersion>> ListMasterVersionsAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_masters.Select(x => x).ToList());
        }
    }

    public Task<PricingMasterVersion> GetActiveMasterVersionAsync()
    {
        lock (_gate)
        {
            var m = _masters.Where(x => x.Status == "Active")
                .OrderByDescending(x => x.EffectiveFromUtc)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("No active PricingMasterVersion — run SeedReferenceDataAsync.");
            return Task.FromResult(m);
        }
    }

    public Task AddMasterVersionAsync(PricingMasterVersion v)
    {
        lock (_gate)
        {
            if (_masters.Any(x => x.VersionRef == v.VersionRef)) return Task.CompletedTask;
            v.MasterVersionId = ++_masterSeq;
            if (v.EffectiveFromUtc == default) v.EffectiveFromUtc = DateTime.UtcNow;
            _masters.Add(v);
            return Task.CompletedTask;
        }
    }

    public Task UpdateMasterVersionAsync(PricingMasterVersion v)
    {
        lock (_gate)
        {
            var existing = _masters.FirstOrDefault(x => x.MasterVersionId == v.MasterVersionId);
            if (existing is null) throw new InvalidOperationException($"Master version {v.MasterVersionId} not found.");
            _masters[_masters.IndexOf(existing)] = v;
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── guardrails ───────────────────────────

    public Task<List<GuardrailRuleDef>> GetGuardrailRulesAsync()
    {
        lock (_gate) return Task.FromResult(_guardrailRules
            .Select(r => new GuardrailRuleDef
            {
                RuleCode = r.RuleCode, Category = r.Category, MetricSelector = r.MetricSelector,
                Operator = r.Operator, Threshold = r.Threshold, Severity = r.Severity,
                MessageTemplate = r.MessageTemplate, AppliesToExperienceBusiness = r.AppliesToExperienceBusiness,
                PctOfMetric = r.PctOfMetric,
            }).ToList());
    }

    public Task UpsertGuardrailRuleAsync(GuardrailRuleDef r)
    {
        lock (_gate)
        {
            var existing = _guardrailRules.FirstOrDefault(x => x.RuleCode == r.RuleCode);
            if (existing is not null)
            {
                existing.Category = r.Category;
                existing.MetricSelector = r.MetricSelector;
                existing.Operator = r.Operator;
                existing.Threshold = r.Threshold;
                existing.Severity = r.Severity;
                existing.MessageTemplate = r.MessageTemplate;
                existing.AppliesToExperienceBusiness = r.AppliesToExperienceBusiness;
                existing.PctOfMetric = r.PctOfMetric;
            }
            else
            {
                _guardrailRules.Add(r);
            }
            return Task.CompletedTask;
        }
    }

    // ─────────────────────────── reference ───────────────────────────

    public Task<Dictionary<string, string>> GetPaidStatusMapAsync()
    {
        lock (_gate) return Task.FromResult(new Dictionary<string, string>(_paidStatus, StringComparer.OrdinalIgnoreCase));
    }
}
