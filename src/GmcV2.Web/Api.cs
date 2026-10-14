using System.Text;
using GmcV2.Calculation;
using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Knowledge;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>JSON API surface (tech §5). Behavior preserved from the verified build.</summary>
public static class Api
{
    public static void MapAll(WebApplication app)
    {
        // ── health ───────────────────────────────────────────────────────────
        app.MapGet("/health", (IGmcRepository repo) => Results.Json(new { status = "ok", version = "v2.0.0", mode = repo is SqlRepository ? "sql" : "memory", utc = DateTime.UtcNow }));

        // ── auth ─────────────────────────────────────────────────────────────
        app.MapPost("/api/v1/auth/login", async (LoginDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest(new { error = "email required" });
            var user = await repo.GetUserByEmailAsync(dto.Email.Trim().ToLowerInvariant());
            if (user is null || !user.IsActive || !PasswordHasher.Verify(dto.Password ?? "", user.PasswordHash))
                return Results.Json(new { error = "invalid credentials" }, statusCode: 401);
            await repo.UpdateLastLoginAsync(user.UserId);
            ctx.Session.SetString("userId", user.UserId.ToString());
            ctx.Session.SetString("role", user.Role);
            ctx.Session.SetString("displayName", user.DisplayName);
            ctx.Session.SetString("email", user.Email);
            return Results.Json(new { user.UserId, user.Email, user.Role, user.DisplayName });
        });

        app.MapPost("/api/v1/auth/logout", (HttpContext ctx) => { ctx.Session.Clear(); return Results.Json(new { ok = true }); });

        app.MapGet("/api/v1/auth/me", (HttpContext ctx) =>
        {
            var uid = ctx.Session.GetString("userId");
            if (uid is null) return Results.Json(new { error = "not authenticated" }, statusCode: 401);
            return Results.Json(new { userId = long.Parse(uid), role = ctx.Session.GetString("role"), displayName = ctx.Session.GetString("displayName") });
        });

        // ── cases ────────────────────────────────────────────────────────────
        app.MapGet("/api/v1/cases", async (IGmcRepository repo, string? status) => Results.Json(await repo.ListCasesAsync(status)));

        app.MapPost("/api/v1/cases", async (CaseDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ClientName)) return Results.BadRequest(new { error = "clientName required" });
            var c = new Case
            {
                ClientName = dto.ClientName.Trim(),
                BusinessType = string.IsNullOrWhiteSpace(dto.BusinessType) ? Enums.BizFresh : dto.BusinessType,
                BusinessSubType = string.IsNullOrWhiteSpace(dto.BusinessSubType) ? "Main" : dto.BusinessSubType,
                Status = Enums.CaseDraft,
                CreatedBy = ctx.Session.GetString("email") ?? "api",
            };
            c = await repo.CreateCaseAsync(c);
            return Results.Json(c);
        });

        app.MapGet("/api/v1/cases/{id:long}", async (long id, IGmcRepository repo) =>
            await repo.GetCaseAsync(id) is { } c ? Results.Json(c) : Results.NotFound(new { error = "case not found" }));

        // ── documents (JSON row upload; real file upload lands with the parser agent) ─
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

        // ── real file upload: multipart/form-data with a 'file' part (+ 'docType') ──
        app.MapPost("/api/v1/cases/{id:long}/documents/upload", async (long id, IGmcRepository repo, HttpContext ctx) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            if (!ctx.Request.HasFormContentType) return Results.BadRequest(new { error = "expected multipart/form-data" });
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "no file part" });
            if (file.Length > 50 * 1024 * 1024) return Results.BadRequest(new { error = "file exceeds 50 MB" });
            var docType = form["docType"].ToString();
            if (string.IsNullOrWhiteSpace(docType)) docType = "Generic";

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var parsed = FileParser.Parse(file.FileName, ms.ToArray());

            var d = new CaseDocument
            {
                CaseId = id,
                DocType = docType,
                FileName = file.FileName,
                ParseStatus = parsed.Error is null ? "Parsed" : "Unreadable",
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray()))[..24],
            };
            d = await repo.AddDocumentAsync(d);

            var rowCount = 0;
            if (parsed.Error is null)
            {
                foreach (var sheet in parsed.Sheets)
                {
                    var all = new List<string[]> { sheet.Headers.ToArray() };
                    all.AddRange(sheet.Rows);
                    await repo.AddRawRowsAsync(d.DocumentId, sheet.SheetName, all);
                    rowCount += sheet.Rows.Count;
                }
            }
            return Results.Json(new
            {
                documentId = d.DocumentId,
                fileName = d.FileName,
                docType = d.DocType,
                parseStatus = d.ParseStatus,
                format = parsed.Format,
                sheets = parsed.Sheets.Count,
                rows = rowCount,
                error = parsed.Error,
            });
        });

        // ── pipeline ─────────────────────────────────────────────────────────
        app.MapPost("/api/v1/cases/{id:long}/pipeline/start", async (long id, IGmcRepository repo, IAIProviderRegistry ai) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            // real uploaded data (parsed files) takes precedence; demo dataset is the fallback
            var c = await repo.GetCaseAsync(id);
            if (c is not null) RealDataBridge.TryRegister(id, c, repo);
            var orch = new PipelineOrchestrator(repo, StageHandlers.Demo(ai), ai);
            await orch.RunAsync(id);
            var fresh = await repo.GetCaseAsync(id); // fresh from DB so StageRuns reflect persisted runs
            var stages = fresh!.StageRuns;
            return Results.Json(new { caseId = id, status = fresh.Status, caseNumber = fresh.CaseNumber, stageCount = stages.Count, stages, dataSource = DemoPipelineData.HasOverride(id) ? "uploaded" : "demo" });
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

        // ── quote artifacts (download) ────────────────────────────────────────
        app.MapGet("/api/v1/quotes/{qvId:long}/artifacts", async (long qvId, IGmcRepository repo) =>
            Results.Json(await repo.GetQuoteArtifactsAsync(qvId)));

        app.MapGet("/api/v1/quotes/{qvId:long}/artifacts/{artifactId:long}/content", async (long qvId, long artifactId, IGmcRepository repo) =>
        {
            var a = await repo.GetQuoteArtifactContentAsync(artifactId);
            if (a is null) return Results.NotFound(new { error = "artifact not found" });
            var mime = a.Value.Type switch
            {
                "json" => "application/json",
                "csv" => "text/csv",
                "html" => "text/html",
                _ => "application/octet-stream",
            };
            return Results.Content(a.Value.Content, mime, System.Text.Encoding.UTF8);
        });

        // ── notifications (in-app center, workflow §12) ──────────────────────
        app.MapGet("/api/v1/notifications", async (IGmcRepository repo, HttpContext ctx) =>
        {
            var role = ctx.Session.GetString("role");
            _ = long.TryParse(ctx.Session.GetString("userId"), out var uid);
            return Results.Json(await repo.GetNotificationsAsync(role, uid));
        });

        app.MapPost("/api/v1/notifications/{id:long}/read", async (long id, IGmcRepository repo) =>
        {
            await repo.MarkNotificationReadAsync(id);
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/v1/notifications/read-all", async (IGmcRepository repo, HttpContext ctx) =>
        {
            var role = ctx.Session.GetString("role");
            _ = long.TryParse(ctx.Session.GetString("userId"), out var uid);
            var n = await repo.MarkAllNotificationsReadAsync(role, uid);
            return Results.Json(new { ok = true, marked = n });
        });

        // ── users (admin) ────────────────────────────────────────────────────
        app.MapGet("/api/v1/users", async (IGmcRepository repo) => Results.Json(await repo.ListUsersAsync()));
    }

    internal record LoginDto(string Email, string Password);
    internal record CaseDto(string ClientName, string? BusinessType, string? BusinessSubType, string? Industry, string? Tpa);
    internal record DocDto(string? FileName, string? DocType, List<List<string>>? Rows);
    internal record AuthorityDto(decimal DeviationPct);
    internal record DecisionDto(long CaseId, string Action, string? Remarks);
}

/// <summary>Authority matrix (workflow §7/§13): ≤5% standard UW scope; above 5% escalates to Senior Underwriter (admin-configurable in full build).</summary>
public static class AuthorityMatrix
{
    public static (string Decision, string RequiredRole) Evaluate(decimal deviationPct) => deviationPct switch
    {
        <= 5m => ("AutoQuote", "Underwriter"),
        _ => ("Escalate", "SeniorUnderwriter"),
    };
}
