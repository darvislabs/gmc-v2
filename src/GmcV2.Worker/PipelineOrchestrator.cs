using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GmcV2.Core;
using GmcV2.Infrastructure;

namespace GmcV2.Worker;

/// <summary>What a stage handler reports back to the orchestrator.</summary>
public sealed class StageOutcome
{
    /// <summary>Pending | Running | Completed | NeedsReview | Failed | Skipped (final persisted state).</summary>
    public string State { get; init; } = Enums.StageCompleted;

    /// <summary>Structured detail persisted on core.CaseStageRun.DetailJson.</summary>
    public string? DetailJson { get; init; }

    /// <summary>Typed exceptions raised by this stage execution.</summary>
    public List<CaseException> ToRaise { get; init; } = new();
}

/// <summary>One stage handler. Pure business logic; persistence stays in the orchestrator.</summary>
public interface IStageHandler
{
    string Stage { get; }
    Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo);
}

/// <summary>
/// Per-case scratch state passed through the handler chain (Guardrail needs the
/// DQ score and mapping confidence computed by earlier stages; Price needs the
/// guardrail verdict; AssembleQuote needs everything). Serialized into the
/// Price stage-run detail so the chain survives a crash.
/// </summary>
public sealed class PerCasePipelineContext
{
    public decimal DataQualityScore { get; set; }
    public decimal MappingConfidencePct { get; set; }
    public AnalyticsSnapshot? Snapshot { get; set; }
    public GuardrailResult? Guardrails { get; set; }
    public PricingResult? Pricing { get; set; }
    public List<BenefitDeltaLine> BenefitDelta { get; set; } = new();
    public string? CaseStatusFromPublish { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The durable 13-stage pipeline state machine (architecture §4.3).
/// Every transition is persisted immediately; a crash resumes from the last
/// completed stage because re-running RunAsync skips stages whose
/// (caseId, stage, InputsHash) already completed with the same hash.
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IGmcRepository _repo;
    private readonly Dictionary<string, IStageHandler> _handlers;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Nullable AI gateway (AD-2): handlers use it to fill gaps only; null/empty = deterministic-only.</summary>
    public IAIProviderRegistry? AI { get; }

    public PipelineOrchestrator(IGmcRepository repo, IStageHandler[] handlers, IAIProviderRegistry? ai = null)
    {
        _repo = repo;
        _handlers = handlers.ToDictionary(h => h.Stage, StringComparer.OrdinalIgnoreCase);
        AI = ai;
    }

    /// <summary>
    /// Auto-start rule (workflow Stage 1): every document required for the case's
    /// business type is present AND parsed. Missing docs park the case in
    /// AwaitingDocuments — that is a checklist state, not an exception.
    /// </summary>
    public static bool ShouldAutoStart(Case c)
    {
        if (!Enums.RequiredDocuments.TryGetValue(c.BusinessType, out var required))
            return false;
        foreach (var docType in required)
        {
            var doc = c.Documents.FirstOrDefault(d =>
                d.DocType.Equals(docType, StringComparison.OrdinalIgnoreCase));
            if (doc is null || !doc.ParseStatus.Equals("Parsed", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Run gate (looser than ShouldAutoStart): documents must be UPLOADED and their
    /// parse attempt finished (any terminal status). Only missing or still-Pending
    /// docs park the case; an Unreadable doc runs into Intake and blocks there as
    /// EXC-DOC-UNREADABLE so the exception path is exercised.
    /// </summary>
    private static bool ShouldProceed(Case c)
    {
        if (!Enums.RequiredDocuments.TryGetValue(c.BusinessType, out var required))
            return false;
        foreach (var docType in required)
        {
            var doc = c.Documents.FirstOrDefault(d =>
                d.DocType.Equals(docType, StringComparison.OrdinalIgnoreCase));
            if (doc is null) return false;
            if (string.IsNullOrWhiteSpace(doc.ParseStatus)
                || doc.ParseStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Idempotency key: document count + document hashes. Any new upload or
    /// re-parse changes the hash and invalidates cached stage runs.
    /// </summary>
    public static string ComputeInputsHash(Case c)
    {
        var sb = new StringBuilder();
        sb.Append(c.Documents.Count).Append('|');
        foreach (var d in c.Documents.OrderBy(d => d.DocumentId))
            sb.Append(d.DocType).Append(':').Append(d.ParseStatus).Append(':').Append(d.Sha256).Append('|');
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    public async Task<Case> RunAsync(long caseId)
    {
        var c = await _repo.GetCaseAsync(caseId)
            ?? throw new InvalidOperationException($"Case {caseId} not found.");

        // Auto-start gate: missing required documents = AwaitingDocuments (not an
        // exception). A doc PRESENT but Unreadable still proceeds — Intake must see
        // it to raise EXC-DOC-UNREADABLE (exceptions are for pipeline failures).
        // Pending/empty parse status = parse not finished → keep waiting.
        if (!ShouldProceed(c))
        {
            if (c.Status is Enums.CaseDraft or Enums.CaseProcessing or Enums.CaseAwaitingDocuments)
            {
                c.Status = Enums.CaseAwaitingDocuments;
                await _repo.UpdateCaseAsync(c);
            }
            return c;
        }

        if (c.Status is Enums.CaseDraft or Enums.CaseAwaitingDocuments)
        {
            c.Status = Enums.CaseProcessing;
            await _repo.UpdateCaseAsync(c);
        }

        var inputsHash = ComputeInputsHash(c);
        var ctx = new PerCasePipelineContext();

        foreach (var stage in Enums.Stages)
        {
            // Idempotency — a completed (or deterministically skipped) run with the
            // same inputs hash is a cache hit.
            var latest = await _repo.GetLatestStageRunAsync(caseId, stage);
            if (latest is not null
                && latest.State is Enums.StageCompleted or Enums.StageSkipped
                && string.Equals(latest.InputsHash, inputsHash, StringComparison.Ordinal))
            {
                HydrateContextFromDetail(ctx, latest.DetailJson);
                continue;
            }

            var attempt = (latest?.AttemptNo ?? 0) + 1;
            c.CurrentStage = stage;
            c.CurrentStageAttempt = attempt;
            await _repo.UpdateCaseAsync(c); // persisted BEFORE running: crash-safe

            var run = await _repo.StartStageRunAsync(caseId, stage, attempt, inputsHash);

            if (!_handlers.TryGetValue(stage, out var handler))
            {
                await _repo.CompleteStageRunAsync(run.StageRunId, Enums.StageSkipped,
                    J(new { reason = "no handler registered" }), 0m);
                continue;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            StageOutcome outcome;
            try
            {
                outcome = await handler.ExecuteAsync(c, _repo);
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Unhandled handler failure = pipeline failure: park it.
                var exc = new CaseException
                {
                    CaseId = caseId,
                    Code = Enums.ExcPipelineFailure,
                    Blocking = true,
                    SubjectType = "Stage",
                    SubjectRef = stage,
                    Message = Truncate(ex.Message, 1000),
                    ResumeStage = stage,
                    ResolutionTarget = "Case Workbench → retry pipeline",
                    Status = "Open",
                };
                await _repo.RaiseExceptionAsync(exc);
                await _repo.CompleteStageRunAsync(run.StageRunId, Enums.StageFailed,
                    J(new { error = Truncate(ex.Message, 500) }), sw.ElapsedMilliseconds / 1000m);
                c.Status = Enums.CaseExceptionPending;
                await _repo.UpdateCaseAsync(c);
                return c;
            }
            sw.Stop();

            // Persist blocking exceptions + park the case (orchestrator STOPS).
            var blocking = outcome.ToRaise.Where(e => e.Blocking).ToList();
            foreach (var e in outcome.ToRaise)
            {
                e.CaseId = caseId;
                e.ResumeStage = string.IsNullOrEmpty(e.ResumeStage) ? stage : e.ResumeStage;
                await _repo.RaiseExceptionAsync(e);
            }

            await _repo.CompleteStageRunAsync(run.StageRunId, outcome.State, outcome.DetailJson,
                Math.Round(sw.ElapsedMilliseconds / 1000m, 3));

            // a failed stage that now completed → auto-resolve its parked pipeline-failure exception
            if (stage == c.CurrentStage)
            {
                try
                {
                    var open = await _repo.GetOpenExceptionsAsync(c.CaseId);
                    foreach (var e in open.Where(e => e.ResumeStage == stage && e.Code == Enums.ExcPipelineFailure))
                        await _repo.ResolveExceptionAsync(e.ExceptionId, "system:stage-recovered");
                }
                catch { /* resolution is best-effort */ }
            }

            HydrateContextFromDetail(ctx, outcome.DetailJson);

            if (blocking.Count > 0)
            {
                c.Status = Enums.CaseExceptionPending;
                await _repo.UpdateCaseAsync(c);
                return c; // STOP — do not advance; ResumeAsync re-enters at this stage
            }

            if (outcome.State == Enums.StageFailed)
            {
                c.Status = Enums.CaseExceptionPending;
                await _repo.UpdateCaseAsync(c);
                return c;
            }

            // Quote publishing: the Publish handler's outcome carries the final case status.
            if (stage == "Publish" && ctx.CaseStatusFromPublish is { Length: > 0 })
            {
                c.Status = ctx.CaseStatusFromPublish;
                await _repo.UpdateCaseAsync(c);
            }
        }

        // Defensive: if Publish did not report a status, derive from the current quote.
        if (c.Status == Enums.CaseProcessing)
        {
            var quote = await _repo.GetCurrentQuoteVersionAsync(caseId);
            c.Status = quote?.Guardrails?.Outcome == "Referred" || quote is not null && quote.Guardrails is not null && quote.Guardrails.Outcome != "AutoQuote"
                ? Enums.CaseReferredToUW
                : Enums.CaseAutoQuoted;
            await _repo.UpdateCaseAsync(c);
        }
        return c;
    }

    /// <summary>
    /// Resume path: after exceptions are resolved, re-enter the pipeline. The
    /// parked stage's last run is not Completed-with-same-hash, so it re-executes.
    /// </summary>
    public async Task<Case> ResumeAsync(long caseId)
    {
        var open = await _repo.GetOpenExceptionsAsync(caseId, blockingOnly: true);
        if (open.Count > 0)
        {
            var c = await _repo.GetCaseAsync(caseId)
                ?? throw new InvalidOperationException($"Case {caseId} not found.");
            return c; // still parked — do nothing until all blocking exceptions are resolved
        }
        return await RunAsync(caseId);
    }

    private static void HydrateContextFromDetail(PerCasePipelineContext ctx, string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson)) return;
        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(detailJson, JsonOpts);
            if (doc is null) return;
            if (doc.TryGetValue("dqScore", out var dq) && dq.ValueKind == JsonValueKind.Number)
                ctx.DataQualityScore = dq.GetDecimal();
            if (doc.TryGetValue("mappingConfidence", out var mc) && mc.ValueKind == JsonValueKind.Number)
                ctx.MappingConfidencePct = mc.GetDecimal();
            if (doc.TryGetValue("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
                ctx.Snapshot = JsonSerializer.Deserialize<AnalyticsSnapshot>(snap.GetRawText(), JsonOpts);
            if (doc.TryGetValue("guardrails", out var grd) && grd.ValueKind == JsonValueKind.Object)
                ctx.Guardrails = JsonSerializer.Deserialize<GuardrailResult>(grd.GetRawText(), JsonOpts);
            if (doc.TryGetValue("pricing", out var prc) && prc.ValueKind == JsonValueKind.Object)
                ctx.Pricing = JsonSerializer.Deserialize<PricingResult>(prc.GetRawText(), JsonOpts);
            if (doc.TryGetValue("benefitDelta", out var bd) && bd.ValueKind == JsonValueKind.Array)
                ctx.BenefitDelta = JsonSerializer.Deserialize<List<BenefitDeltaLine>>(bd.GetRawText(), JsonOpts) ?? new();
            if (doc.TryGetValue("caseStatus", out var cs) && cs.ValueKind == JsonValueKind.String)
                ctx.CaseStatusFromPublish = cs.GetString();
        }
        catch (JsonException)
        {
            // Detail is advisory state; the pipeline continues without it.
        }
    }

    private static string J<T>(T v) => JsonSerializer.Serialize(v, JsonOpts);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
