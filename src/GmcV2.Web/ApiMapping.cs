using GmcV2.Core;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>
/// Column mapping API — backs the #/mapping workspace (workflow §6.2 EXC-MAP-*,
/// tech §5.3). All decisions are human-audited: retarget/clear/resolve-conflict write
/// an append-only ing.ColumnMapping row (Confidence 1.00, TierHumanConfirmed) and pin
/// ing.SourceColumn.TargetParameter so force pipeline re-runs never clobber them.
/// </summary>
public static class ApiMapping
{
    public static void MapAll(WebApplication app)
    {
        // ── cases that carry profiled source columns ─────────────────────────────
        app.MapGet("/api/v1/mapping/cases", async (IGmcRepository repo) =>
        {
            var result = new List<object>();
            foreach (var c in (await repo.ListCasesAsync()).OrderByDescending(x => x.CaseId))
            {
                var docCount = 0;
                var mapped = 0;
                var unmapped = 0;
                foreach (var d in c.Documents.Where(d => d.ParseStatus == "Parsed"))
                {
                    var cols = await repo.GetSourceColumnsAsync(d.DocumentId);
                    if (cols.Count == 0) continue;
                    docCount++;
                    foreach (var col in cols)
                        if (col.TargetParameter is null or "" or "Unmapped") unmapped++;
                        else mapped++;
                }
                if (docCount > 0)
                    result.Add(new
                    {
                        caseId = c.CaseId,
                        caseNumber = c.CaseNumber,
                        clientName = c.ClientName,
                        status = c.Status,
                        documentCount = docCount,
                        mappedColumns = mapped,
                        unmappedColumns = unmapped,
                    });
            }
            return Results.Json(result);
        });

        // ── per-case mapping detail: documents → columns (+ current mapping) ─────
        app.MapGet("/api/v1/mapping/{caseId:long}", async (long caseId, IGmcRepository repo) =>
        {
            var c = await repo.GetCaseAsync(caseId);
            if (c is null) return Results.NotFound(new { error = "case not found" });

            var documents = new List<object>();
            var mappedCount = 0;
            var unmappedCount = 0;
            var conflicts = new List<object>();
            foreach (var d in c.Documents.Where(d => d.ParseStatus == "Parsed").OrderBy(d => d.DocumentId))
            {
                var cols = await repo.GetSourceColumnsAsync(d.DocumentId);
                if (cols.Count == 0) continue;
                var rows = new List<object>();
                // duplicate-target conflicts are scoped PER DOCUMENT: StandardizationEngine
                // builds one column plan per document, so only same-document collisions
                // are ambiguous. The same parameter in two documents (claims + enrollment
                // Member ID) is the normal linkage pattern, not a conflict.
                var byTarget = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in cols.OrderBy(x => x.ColumnIndex))
                {
                    var latest = await repo.GetLatestMappingAsync(col.SourceColumnId);
                    var target = col.TargetParameter is null or "" or "Unmapped" ? null : col.TargetParameter;
                    if (target is not null)
                    {
                        mappedCount++;
                        if (!byTarget.TryGetValue(target, out var claimants)) byTarget[target] = claimants = new();
                        claimants.Add(col.SourceColumnId);
                    }
                    else unmappedCount++;
                    rows.Add(new
                    {
                        sourceColumnId = col.SourceColumnId,
                        column = col.ColumnIndex,
                        header = col.Header,
                        detectedType = col.DetectedType,
                        nonEmpty = col.NonEmptyCount,
                        distinct = col.DistinctCount,
                        samples = col.SampleValues.Take(3),
                        targetParameter = target,
                        confidence = col.Confidence,
                        provenanceTier = col.ProvenanceTier,
                        reason = col.Reason,
                        isRequired = col.IsRequired,
                        lastDecisionBy = latest?.ApprovedBy,
                        lastDecisionAtUtc = latest?.CreatedAtUtc,
                    });
                }
                foreach (var kv in byTarget.Where(kv => kv.Value.Count > 1))
                    conflicts.Add(new { documentId = d.DocumentId, docType = d.DocType, targetParameter = kv.Key, sourceColumnIds = kv.Value });
                documents.Add(new
                {
                    documentId = d.DocumentId,
                    docType = d.DocType,
                    fileName = d.FileName,
                    parseStatus = d.ParseStatus,
                    columns = rows,
                });
            }

            return Results.Json(new
            {
                caseId = c.CaseId,
                caseNumber = c.CaseNumber,
                clientName = c.ClientName,
                documents,
                counts = new { documents = documents.Count, mapped = mappedCount, unmapped = unmappedCount, conflicts = conflicts.Count },
                conflicts,
                parameters = MappingParameters.All,
            });
        });

        // ── retarget one column (or clear it) ───────────────────────────────────
        app.MapPost("/api/v1/mapping/{caseId:long}/retarget", async (long caseId, RetargetDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (await repo.GetCaseAsync(caseId) is null) return Results.NotFound(new { error = "case not found" });
            if (dto.SourceColumnId <= 0) return Results.BadRequest(new { error = "sourceColumnId required" });

            var c = (await repo.GetCaseAsync(caseId))!;
            var col = await FindColumnAsync(c, repo, dto.SourceColumnId);
            if (col is null) return Results.NotFound(new { error = "source column not found in this case" });

            var newTarget = string.IsNullOrWhiteSpace(dto.TargetParameter) || dto.TargetParameter == "Unmapped"
                ? null
                : dto.TargetParameter.Trim();
            if (newTarget is not null && !MappingParameters.All.Contains(newTarget, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"unknown target parameter '{newTarget}' — valid: {string.Join(", ", MappingParameters.All)}" });

            // conflict guard: another ACTIVE column in the SAME document already claims
            // the target (BuildPlan is per-document; cross-document reuse is normal)
            if (newTarget is not null)
            {
                var claimant = await FindClaimantAsync(col.DocumentId, repo, newTarget, excludeColumnId: col.SourceColumnId);
                if (claimant is not null)
                    return Results.Json(new
                    {
                        error = $"parameter '{newTarget}' is already claimed by column '{claimant.Header}' — resolve the conflict first",
                        conflict = new { targetParameter = newTarget, conflictingSourceColumnId = claimant.SourceColumnId, conflictingHeader = claimant.Header },
                    }, statusCode: 409);
            }

            var user = ctx.Session.GetString("email") ?? dto.UserId ?? "mapping-workbench";
            col.TargetParameter = newTarget;
            col.Confidence = 1.00m;
            col.ProvenanceTier = Enums.TierHumanConfirmed;
            col.Reason = newTarget is null ? "Human cleared mapping in Mapping Workbench" : $"Human retarget in Mapping Workbench → {newTarget}";
            col.IsRequired = newTarget is not null && MappingParameters.IsRequired(newTarget);
            await repo.AddOrUpdateSourceColumnAsync(col);
            await repo.AddColumnMappingAsync(new ColumnMapping
            {
                SourceColumnId = col.SourceColumnId,
                TargetParameter = newTarget ?? "Unmapped",
                Confidence = 1.00m,
                ProvenanceTier = Enums.TierHumanConfirmed,
                AutoApproved = true,
                ApprovedBy = user,
            });

            return Results.Json(new
            {
                ok = true,
                sourceColumnId = col.SourceColumnId,
                header = col.Header,
                targetParameter = newTarget,
                confidence = col.Confidence,
                provenanceTier = col.ProvenanceTier,
                decidedBy = user,
            });
        });

        // ── conflict resolution: winner keeps the target, loser is cleared ──────
        app.MapPost("/api/v1/mapping/{caseId:long}/resolve-conflict", async (long caseId, ResolveConflictDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (await repo.GetCaseAsync(caseId) is null) return Results.NotFound(new { error = "case not found" });
            if (dto.WinnerSourceColumnId <= 0 || dto.LoserSourceColumnId <= 0)
                return Results.BadRequest(new { error = "winnerSourceColumnId and loserSourceColumnId required" });
            if (dto.WinnerSourceColumnId == dto.LoserSourceColumnId)
                return Results.BadRequest(new { error = "winner and loser must be different columns" });

            var c = (await repo.GetCaseAsync(caseId))!;
            var cols = new List<SourceColumn>();
            foreach (var d in c.Documents.Where(d => d.ParseStatus == "Parsed"))
                cols.AddRange(await repo.GetSourceColumnsAsync(d.DocumentId));

            var winner = cols.FirstOrDefault(x => x.SourceColumnId == dto.WinnerSourceColumnId);
            if (winner is null) return Results.NotFound(new { error = "winner source column not found" });
            var loser = cols.FirstOrDefault(x => x.SourceColumnId == dto.LoserSourceColumnId);
            if (loser is null) return Results.NotFound(new { error = "loser source column not found" });

            var target = winner.TargetParameter is { } wt && wt != "Unmapped" ? wt : null;
            if (target is null) return Results.BadRequest(new { error = "winner column has no active target parameter" });
            if (!string.Equals(loser.TargetParameter, target, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"loser does not claim '{target}' — nothing to resolve" });
            if (winner.DocumentId != loser.DocumentId)
                return Results.BadRequest(new { error = "winner and loser are in different documents — conflicts are per-document; clear the unwanted mapping instead" });

            var user = ctx.Session.GetString("email") ?? dto.UserId ?? "mapping-workbench";
            loser.TargetParameter = null;
            loser.Confidence = 1.00m;
            loser.ProvenanceTier = Enums.TierHumanConfirmed;
            loser.Reason = $"Human conflict resolution in Mapping Workbench — '{target}' awarded to column #{winner.SourceColumnId} ({winner.Header})";
            loser.IsRequired = false;
            await repo.AddOrUpdateSourceColumnAsync(loser);
            // loser's row records the human decision (append-only audit trail)
            await repo.AddColumnMappingAsync(new ColumnMapping
            {
                SourceColumnId = loser.SourceColumnId,
                TargetParameter = "Unmapped",
                Confidence = 1.00m,
                ProvenanceTier = Enums.TierHumanConfirmed,
                AutoApproved = true,
                ApprovedBy = user,
            });
            // winner's claim is re-asserted as human-confirmed
            winner.Confidence = 1.00m;
            winner.ProvenanceTier = Enums.TierHumanConfirmed;
            winner.Reason = $"Human conflict resolution in Mapping Workbench — kept '{target}' over column #{loser.SourceColumnId} ({loser.Header})";
            await repo.AddOrUpdateSourceColumnAsync(winner);
            await repo.AddColumnMappingAsync(new ColumnMapping
            {
                SourceColumnId = winner.SourceColumnId,
                TargetParameter = target,
                Confidence = 1.00m,
                ProvenanceTier = Enums.TierHumanConfirmed,
                AutoApproved = true,
                ApprovedBy = user,
            });

            return Results.Json(new
            {
                ok = true,
                targetParameter = target,
                winner = new { sourceColumnId = winner.SourceColumnId, header = winner.Header },
                loser = new { sourceColumnId = loser.SourceColumnId, header = loser.Header, targetParameter = (string?)null },
                decidedBy = user,
            });
        });
    }

    private static async Task<SourceColumn?> FindColumnAsync(Case c, IGmcRepository repo, long sourceColumnId)
    {
        foreach (var d in c.Documents.Where(d => d.ParseStatus == "Parsed"))
        {
            var col = (await repo.GetSourceColumnsAsync(d.DocumentId))
                .FirstOrDefault(x => x.SourceColumnId == sourceColumnId);
            if (col is not null) return col;
        }
        return null;
    }

    private static async Task<SourceColumn?> FindClaimantAsync(long documentId, IGmcRepository repo, string targetParameter, long excludeColumnId)
    {
        var col = (await repo.GetSourceColumnsAsync(documentId))
            .FirstOrDefault(x => x.SourceColumnId != excludeColumnId
                && x.TargetParameter is { } t && t != "Unmapped"
                && t.Equals(targetParameter, StringComparison.OrdinalIgnoreCase));
        return col;
    }

    internal record RetargetDto(string? TargetParameter, long SourceColumnId, string? UserId);
    internal record ResolveConflictDto(long WinnerSourceColumnId, long LoserSourceColumnId, string? UserId);
}
