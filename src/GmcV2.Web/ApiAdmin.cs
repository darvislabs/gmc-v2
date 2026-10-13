using GmcV2.Core;
using GmcV2.Infrastructure;
using Microsoft.Data.SqlClient;

namespace GmcV2.Web;

/// <summary>Admin + ops API: audit log, master versions, AI gateway status, pipeline stats.</summary>
public static class ApiAdmin
{
    public static void MapAll(WebApplication app)
    {
        // ── audit log (ops.AuditLog, newest first) ───────────────────────────
        app.MapGet("/api/v1/admin/audit", async (IGmcRepository repo, int limit) =>
        {
            var list = new List<object>();
            if (repo is SqlRepository sql)
                list = await Audit.QueryAsync(sql, limit <= 0 ? 100 : Math.Min(limit, 500));
            return Results.Json(list);
        });

        // ── pricing master versions (uw.PricingMasterVersion) ────────────────
        app.MapGet("/api/v1/admin/masters", async (IGmcRepository repo) =>
        {
            var masters = await repo.ListMasterVersionsAsync();
            return Results.Json(masters.Select(m => new { m.MasterVersionId, m.VersionRef, m.Status, m.EffectiveFromUtc, m.EffectiveToUtc }));
        });

        // ── AI gateway status (AD-1/AD-2: provider wiring visibility) ────────
        app.MapGet("/api/v1/admin/ai-status", (IAIProviderRegistry registry) => Results.Json(new
        {
            extraction = registry.Extraction?.IsAvailable ?? false,
            mapping = registry.Mapping?.IsAvailable ?? false,
            clinical = registry.Clinical?.IsAvailable ?? false,
            narrative = registry.Narrative?.IsAvailable ?? false,
            anyAvailable = registry.AnyAvailable,
            mode = registry.AnyAvailable ? "hybrid" : "deterministic-only",
            note = "Wire a provider (NVIDIA NIM / OpenAI / Gemini) via GMCV2_AI_PROVIDER + key — agent contracts are live in GmcV2.Infrastructure.AIGateway.",
        }));

        // ── pipeline stats (per-stage timings from the last runs) ────────────
        app.MapGet("/api/v1/admin/stats", async (IGmcRepository repo) =>
        {
            var cases = await repo.ListCasesAsync();
            var byStatus = cases.GroupBy(c => c.Status).ToDictionary(g => g.Key, g => g.Count());
            int autoQuoted = byStatus.GetValueOrDefault(Enums.CaseAutoQuoted);
            int referred = byStatus.GetValueOrDefault(Enums.CaseReferredToUW);
            int total = cases.Count;
            return Results.Json(new
            {
                totalCases = total,
                byStatus,
                straightThroughRatePct = total > 0 ? Math.Round(autoQuoted * 100.0m / total, 1) : 0,
                referralRatePct = total > 0 ? Math.Round(referred * 100.0m / total, 1) : 0,
            });
        });
    }
}

/// <summary>Audit log reader (ops.AuditLog) — kept out of IGmcRepository to avoid touching both repos.</summary>
public static class Audit
{
    public static async Task<List<object>> QueryAsync(SqlRepository repo, int limit)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnectionStringHolder.Current);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TOP (@n) AuditLogId, CorrelationId, EntityType, EntityId, CaseId, Action, ActorType, ActorId, CreatedAtUtc
              FROM ops.AuditLog ORDER BY AuditLogId DESC
            """;
        cmd.Parameters.AddWithValue("@n", limit);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new
            {
                auditLogId = r["AuditLogId"] is DBNull ? 0L : (long)Convert.ToInt64(r["AuditLogId"]),
                correlationId = r["CorrelationId"] is DBNull ? null : (string)r["CorrelationId"],
                entityType = r["EntityType"] is DBNull ? null : (string)r["EntityType"],
                entityId = r["EntityId"] is DBNull ? null : (long?)Convert.ToInt64(r["EntityId"]),
                caseId = r["CaseId"] is DBNull ? null : (long?)Convert.ToInt64(r["CaseId"]),
                action = r["Action"] is DBNull ? null : (string)r["Action"],
                actorType = r["ActorType"] is DBNull ? null : (string)r["ActorType"],
                actorId = r["ActorId"] is DBNull ? null : (string)r["ActorId"],
                createdAtUtc = r["CreatedAtUtc"] is DBNull ? DateTime.MinValue : (DateTime)r["CreatedAtUtc"],
            });
        return list;
    }
}

/// <summary>Holds the current SQL connection string for admin-side direct queries.</summary>
public static class ConnectionStringHolder
{
    public static string Current { get; private set; } = "";
    public static void Set(string cs) => Current = cs;
}
