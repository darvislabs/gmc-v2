using System.Text;
using GmcV2.Calculation;
using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Knowledge;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>
/// GMC v2 API + console host (technical_code_and_pages.md §5).
/// JSON API under /api/v1, console UI at /, /health for LB.
/// SQL Server (docker GMCv2) when GMCV2_SQL is set; in-memory otherwise.
/// </summary>
public static class Program
{
    static IGmcRepository Repo()
    {
        var sql = Environment.GetEnvironmentVariable("GMCV2_SQL");
        if (!string.IsNullOrWhiteSpace(sql)) return new SqlRepository(sql);
        return new InMemoryRepository();
    }

    public static async Task<int> Main(string[] args)
    {
        var repo = Repo();
        if (repo is InMemoryRepository mem) await SeedRunner.SeedReferenceDataAsync(mem);

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(repo);
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        var app = builder.Build();
        app.UseCors();

        // ── health ───────────────────────────────────────────────────────────
        app.MapGet("/health", () => Results.Json(new { status = "ok", version = "v2.0.0", mode = repo is SqlRepository ? "sql" : "memory", utc = DateTime.UtcNow }));

        // ── auth (demo identity; role derived from email, v1-compatible shape) ─
        app.MapPost("/api/v1/auth/login", (LoginDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest(new { error = "email required" });
            var e = dto.Email.ToLowerInvariant();
            var role = e.Contains("senior") ? "SeniorUnderwriter" : e.Contains("uw") || e.Contains("underwriting") ? "Underwriter"
                     : e.Contains("admin") ? "Admin" : "SalesAgent";
            return Results.Json(new { token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(e)))[..32], role, email = dto.Email });
        });

        // ── cases ────────────────────────────────────────────────────────────
        app.MapGet("/api/v1/cases", async (IGmcRepository repo, string? status) => Results.Json(await repo.ListCasesAsync(status)));

        app.MapPost("/api/v1/cases", async (CaseDto dto, IGmcRepository repo) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ClientName)) return Results.BadRequest(new { error = "clientName required" });
            var c = new Case
            {
                ClientName = dto.ClientName,
                BusinessType = string.IsNullOrWhiteSpace(dto.BusinessType) ? Enums.BizFresh : dto.BusinessType,
                BusinessSubType = string.IsNullOrWhiteSpace(dto.BusinessSubType) ? "Main" : dto.BusinessSubType,
                Status = Enums.CaseDraft,
                CreatedBy = "api",
            };
            c = await repo.CreateCaseAsync(c);
            return Results.Json(c);
        });

        app.MapGet("/api/v1/cases/{id:long}", async (long id, IGmcRepository repo) =>
            await repo.GetCaseAsync(id) is { } c ? Results.Json(c) : Results.NotFound(new { error = "case not found" }));

        // ── documents (metadata persisted; demo pipeline carries its own dataset) ─
        app.MapPost("/api/v1/cases/{id:long}/documents", async (long id, DocDto dto, IGmcRepository repo) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            var rows = dto.Rows ?? new List<List<string>>();
            var d = new CaseDocument
            {
                CaseId = id,
                DocType = string.IsNullOrWhiteSpace(dto.DocType) ? "Generic" : dto.DocType,
                FileName = string.IsNullOrWhiteSpace(dto.FileName) ? "upload" : dto.FileName,
                ParseStatus = rows.Count > 0 ? "Parsed" : "Pending",
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dto.FileName + rows.Count)))[..24],
            };
            d = await repo.AddDocumentAsync(d);
            return Results.Json(d);
        });

        app.MapGet("/api/v1/cases/{id:long}/documents", async (long id, IGmcRepository repo) =>
            await repo.GetCaseAsync(id) is { } c ? Results.Json(c.Documents) : Results.NotFound(new { error = "case not found" }));

        // ── pipeline ─────────────────────────────────────────────────────────
        app.MapPost("/api/v1/cases/{id:long}/pipeline/start", async (long id, IGmcRepository repo) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            var orch = new PipelineOrchestrator(repo, StageHandlers.Demo());
            await orch.RunAsync(id);
            var c = await repo.GetCaseAsync(id); // fresh from DB so StageRuns reflect persisted runs
            var stages = c!.StageRuns;
            return Results.Json(new { caseId = id, status = c.Status, caseNumber = c.CaseNumber, stageCount = stages.Count, stages });
        });

        app.MapGet("/api/v1/cases/{id:long}/pipeline", async (long id, IGmcRepository repo) =>
            await repo.GetCaseAsync(id) is { } c ? Results.Json(c.StageRuns) : Results.NotFound(new { error = "case not found" }));

        // ── quotes (full pricing bridge + analytics + guardrails nested) ──────
        app.MapGet("/api/v1/cases/{id:long}/quotes", async (long id, IGmcRepository repo) => Results.Json(await repo.GetQuoteVersionsAsync(id)));

        app.MapGet("/api/v1/cases/{id:long}/guardrails", async (long id, IGmcRepository repo) =>
        {
            var c = await repo.GetCaseAsync(id);
            if (c is null) return Results.NotFound(new { error = "case not found" });
            var quote = (await repo.GetQuoteVersionsAsync(id)).FirstOrDefault(q => q.Status == Enums.QuoteCurrent);
            if (quote?.Analytics is { } snap)
            {
                var rules = await repo.GetGuardrailRulesAsync();
                return Results.Json(GuardrailEngine.Evaluate(snap, rules, dqScore: 96m, mappingConfidence: 99m, c.BusinessType));
            }
            return Results.Json(quote?.Guardrails ?? new GuardrailResult());
        });

        // ── exceptions ───────────────────────────────────────────────────────
        app.MapGet("/api/v1/exceptions", async (IGmcRepository repo, long? caseId) =>
        {
            if (caseId is { } cid) return Results.Json(await repo.GetOpenExceptionsAsync(cid));
            var all = new List<CaseException>();
            foreach (var c in await repo.ListCasesAsync())
                all.AddRange(await repo.GetOpenExceptionsAsync(c.CaseId));
            return Results.Json(all);
        });

        app.MapPost("/api/v1/exceptions/{id:long}/resolve", async (long id, long caseId, IGmcRepository repo) =>
        {
            await repo.ResolveExceptionAsync(id, "api");
            return Results.Json(new { ok = true, exceptionId = id });
        });

        // ── underwriting authority + decisions ───────────────────────────────
        app.MapPost("/api/v1/uw/authority/check", (AuthorityDto dto) =>
        {
            var (decision, requiredRole) = AuthorityMatrix.Evaluate(dto.DeviationPct);
            return Results.Json(new { decision, requiredRole, deviationPct = dto.DeviationPct });
        });

        app.MapPost("/api/v1/uw/decisions", async (DecisionDto dto, IGmcRepository repo) =>
        {
            var valid = new[] { "Approve", "ApproveWithChanges", "Reject", "Decline", "Revise", "Escalate", "ManualQuote", "ReturnForClarification", "NeedMoreInfo" };
            if (!valid.Contains(dto.Action, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"action must be one of {string.Join(", ", valid)}" });
            var c = await repo.GetCaseAsync(dto.CaseId);
            if (c is null) return Results.NotFound(new { error = "case not found" });
            var quote = (await repo.GetQuoteVersionsAsync(dto.CaseId)).FirstOrDefault(q => q.Status == Enums.QuoteCurrent);
            await repo.AddUnderwritingActionAsync(new UnderwritingAction
            {
                CaseId = dto.CaseId,
                QuoteVersionId = (int)(quote?.QuoteVersionId ?? 0),
                Action = dto.Action,
                Actor = "api",
                Remarks = dto.Remarks,
            });
            c.Status = dto.Action.ToLowerInvariant() switch
            {
                "approve" => Enums.CaseApproved,
                "approvewithchanges" => Enums.CaseApprovedWithChanges,
                "reject" or "decline" => Enums.CaseDeclined,
                _ => c.Status,
            };
            await repo.UpdateCaseAsync(c);
            return Results.Json(new { caseId = dto.CaseId, caseStatus = c.Status, action = dto.Action });
        });

        // ── console UI (Opexminds shell: sidebar + topbar + KPIs + case table) ─
        app.MapGet("/", async (IGmcRepository repo) =>
        {
            var cases = await repo.ListCasesAsync();
            int autoQuoted = cases.Count(c => c.Status == Enums.CaseAutoQuoted);
            int referred = cases.Count(c => c.Status == Enums.CaseReferredToUW);
            int drafts = cases.Count(c => c.Status == Enums.CaseDraft || c.Status == Enums.CaseAwaitingDocuments);
            int approved = cases.Count(c => c.Status == Enums.CaseApproved || c.Status == Enums.CaseApprovedWithChanges);
            string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

            string Badge(string status) => status switch
            {
                Enums.CaseAutoQuoted => """<span class="ox-badge ox-badge--ok">AutoQuoted</span>""",
                Enums.CaseApproved or Enums.CaseApprovedWithChanges => """<span class="ox-badge ox-badge--electric">Approved</span>""",
                Enums.CaseReferredToUW => """<span class="ox-badge ox-badge--warn">ReferredToUW</span>""",
                Enums.CaseExceptionPending => """<span class="ox-badge ox-badge--danger">Exception</span>""",
                Enums.CaseDeclined => """<span class="ox-badge ox-badge--danger">Declined</span>""",
                _ => """<span class="ox-badge ox-badge--neutral">Draft</span>""",
            };

            var sb = new StringBuilder();
            sb.Append("""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GMC v2 — Opexminds Underwriting Console</title><link rel="stylesheet" href="/css/opex.css"></head><body>""");
            sb.Append("""<div class="ox-shell"><aside class="ox-sidebar"><a class="ox-brand" href="/">GMC <span class="ox-brand-sub">v2.0 · Opexminds</span></a><nav class="ox-nav"><div class="ox-nav-section">Underwriting</div><a class="ox-nav-item active" href="/">Cases</a><a class="ox-nav-item" href="/api/v1/exceptions">Exceptions</a><div class="ox-nav-section">System</div><a class="ox-nav-item" href="/health">Health</a></nav><div class="ox-sidebar-footer"><span class="ox-user-name">Darvis</span><span class="ox-user-role">Underwriting Ops</span></div></aside>""");
            sb.Append("""<div class="ox-main"><header class="ox-topbar"><h1 class="ox-pagetitle">Cases</h1></header><div class="ox-content">""");
            sb.Append($$"""
                <div class="ox-kpi-row">
                  <div class="ox-kpi ox-kpi--primary"><div class="ox-mono-label">TOTAL CASES</div><div class="ox-kpi-num">{{cases.Count}}</div></div>
                  <div class="ox-kpi ox-kpi--ok"><div class="ox-mono-label">AUTO-QUOTED</div><div class="ox-kpi-num">{{autoQuoted}}</div></div>
                  <div class="ox-kpi ox-kpi--warn"><div class="ox-mono-label">REFERRED TO UW</div><div class="ox-kpi-num">{{referred}}</div></div>
                  <div class="ox-kpi ox-kpi--electric"><div class="ox-mono-label">APPROVED</div><div class="ox-kpi-num">{{approved}}</div></div>
                  <div class="ox-kpi"><div class="ox-mono-label">IN PROGRESS</div><div class="ox-kpi-num">{{drafts}}</div></div>
                </div>
                """);
            if (cases.Count == 0)
                sb.Append("""<div class="ox-empty-state"><p class="ox-mb-0">No cases yet. Create one via POST /api/v1/cases — the 13-stage pipeline runs automatically once documents land.</p></div>""");
            else
            {
                sb.Append("""<div class="ox-table-wrap"><table class="ox-table"><thead><tr><th>Case</th><th>Client</th><th>Type</th><th>Stage</th><th>Status</th></tr></thead><tbody>""");
                foreach (var c in cases.Take(100))
                    sb.Append($$"""<tr><td class="ox-mono">{{c.CaseNumber}}</td><td>{{Esc(c.ClientName)}}</td><td>{{Esc(c.BusinessType)}} · {{Esc(c.BusinessSubType)}}</td><td>{{Esc(c.CurrentStage ?? "—")}}</td><td>{{Badge(c.Status)}}</td></tr>""");
                sb.Append("""</tbody></table></div>""");
            }
            sb.Append("""<div class="ox-footer">GMC v2.0 · deterministic underwriting core · Opexminds design system</div></div></div></div></body></html>""");
            return Results.Content(sb.ToString(), "text/html; charset=utf-8");
        });

        app.UseStaticFiles();
        app.Run();
        return 0;
    }

    record LoginDto(string Email, string Password);
    record CaseDto(string ClientName, string? BusinessType, string? BusinessSubType, string? Industry, string? Tpa);
    record DocDto(string? FileName, string? DocType, List<List<string>>? Rows);
    record AuthorityDto(decimal DeviationPct);
    record DecisionDto(long CaseId, string Action, string? Remarks);
}

/// <summary>Authority matrix (workflow §7/§13): ≤5% standard UW scope; above 5% the case escalates — e.g. the §13 worked example records 7.9% deviation escalating to a Senior Underwriter. Admin-configurable in full build.</summary>
public static class AuthorityMatrix
{
    public static (string Decision, string RequiredRole) Evaluate(decimal deviationPct) => deviationPct switch
    {
        <= 5m => ("AutoQuote", "Underwriter"),
        _ => ("Escalate", "SeniorUnderwriter"),
    };
}
