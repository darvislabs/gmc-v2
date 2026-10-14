using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Admin rule configurator (prototype §6/§7 admin panels): list, create, edit, enable/disable
/// guardrail rules — the engine is data-driven, so a rule edit is a row edit. Admin-only.</summary>
public static class RuleConfigApi
{
    public sealed record RuleUpsertDto(
        string? RuleCode, string? RuleName, string Category, string MetricSelector,
        string Operator, decimal Threshold, string Severity, string MessageTemplate,
        bool AppliesToExperienceBusiness, bool IsEnabled);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/admin/guardrail-rules", async (IGmcRepository repo) =>
            Results.Json(await repo.GetAllGuardrailRulesAsync()));

        app.MapPost("/api/v1/admin/guardrail-rules", async (RuleUpsertDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(dto.RuleCode)) return Results.BadRequest(new { error = "RuleCode required" });
            if (dto.Threshold < 0) return Results.BadRequest(new { error = "threshold must be ≥ 0" });
            await repo.UpsertGuardrailRuleAsync(new GuardrailRuleDef
            {
                RuleCode = dto.RuleCode.Trim(),
                RuleName = dto.RuleName ?? dto.RuleCode.Trim(),
                Category = dto.Category,
                MetricSelector = dto.MetricSelector,
                Operator = dto.Operator,
                Threshold = dto.Threshold,
                Severity = dto.Severity,
                MessageTemplate = dto.MessageTemplate,
                AppliesToExperienceBusiness = dto.AppliesToExperienceBusiness,
                IsEnabled = dto.IsEnabled,
            });
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/v1/admin/guardrail-rules/{code}/toggle", async (string code, IGmcRepository repo, HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.StatusCode(403);
            var rules = await repo.GetAllGuardrailRulesAsync();
            var rule = rules.FirstOrDefault(r => string.Equals(r.RuleCode, code, StringComparison.OrdinalIgnoreCase));
            if (rule is null) return Results.NotFound(new { error = "rule not found" });
            rule.IsEnabled = !rule.IsEnabled;
            await repo.UpsertGuardrailRuleAsync(rule);
            return Results.Json(new { ok = true, ruleCode = rule.RuleCode, isEnabled = rule.IsEnabled });
        });
    }

    private static bool IsAdmin(HttpContext ctx) =>
        string.Equals(ctx.Session.GetString("role"), "Admin", StringComparison.OrdinalIgnoreCase);
}
