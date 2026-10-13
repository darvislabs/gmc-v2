using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// Stage 8 — guardrail evaluation (db.md §10). Rules are data (GuardrailRuleDef);
/// the engine is a deterministic metric-selector evaluator. Fresh business marks
/// experience-dependent rules NotApplicable rather than failed (workflow §10).
/// Bridge-compatible: static Evaluate(snap, rules, dq, mapConf, businessType).
/// </summary>
public sealed class GuardrailEngine
{
    public GuardrailEngine() { }

    public static GuardrailResult Evaluate(AnalyticsSnapshot snap, List<GuardrailRuleDef> rules,
        decimal dqScore, decimal mappingConfidence, string businessType)
    {
        var experienceDependent = businessType is "Rollover" or "Renewal-External" or "Renewal-Internal";
        var evals = new List<GuardrailEvaluation>();
        var ratePerLife = snap.CoveredLives > 0 ? snap.AmountIncurred / snap.CoveredLives : 0m;

        foreach (var rule in rules)
        {
            // fresh business: experience-only rules are NotApplicable, never failed (workflow §10)
            if (!experienceDependent && rule.AppliesToExperienceBusiness)
            {
                evals.Add(new GuardrailEvaluation
                {
                    RuleCode = rule.RuleCode,
                    Category = rule.Category,
                    Operator = rule.Operator,
                    Threshold = rule.Threshold,
                    Observed = 0m,
                    Passed = true,
                    Severity = "NotApplicable",
                    Message = "Not applicable — fresh business has no claims experience.",
                });
                continue;
            }

            var observed = rule.MetricSelector switch
            {
                "CoveredLives" => snap.CoveredLives,
                "ClaimCount" => snap.ClaimCount,
                "DataQualityScore" => dqScore,
                "MappingConfidenceAvg" => mappingConfidence,
                "VolatilityCv" => snap.VolatilityCv,
                "Top5ClaimSharePct" => snap.Top5ClaimSharePct,
                "LargeClaimSharePct" => snap.LargeClaimSharePct,
                "SingleLargestSharePct" => snap.SingleLargestSharePct,
                "AvgAge" => snap.AvgAge,
                "SeniorAgeSharePct" => snap.SeniorAgeSharePct,
                "ParentSharePct" => snap.ParentSharePct,
                "ChronicPrevalencePct" => snap.ChronicPrevalencePct,
                "MaternitySharePct" => snap.MaternitySharePct,
                "NonNetworkSharePct" => snap.NonNetworkSharePct,
                "RatePerLifeBeforeGst" => ratePerLife,
                _ => 0m,
            };
            var passed = rule.Operator switch
            {
                ">=" => observed >= rule.Threshold,
                "<=" => observed <= rule.Threshold,
                "<" => observed < rule.Threshold,
                ">" => observed > rule.Threshold,
                _ => true,
            };
            evals.Add(new GuardrailEvaluation
            {
                RuleCode = rule.RuleCode,
                Category = rule.Category,
                Operator = rule.Operator,
                Threshold = rule.Threshold,
                Observed = Math.Round(observed, 4),
                Passed = passed,
                Severity = rule.Severity,
                Message = rule.MessageTemplate.Replace("{observed}", Math.Round(observed, 2).ToString("0.##")),
            });
        }

        var applicable = evals.Where(e => e.Severity != "NotApplicable").ToList();
        var passRate = applicable.Count == 0 ? 100m : (decimal)applicable.Count(e => e.Passed) / applicable.Count * 100m;
        var result = new GuardrailResult { Evaluations = evals };
        // Confidence = DQ 50% + mapping 30% + rule pass rate 20% (INFRW-compatible blend)
        result.Confidence = Math.Round(dqScore * 0.5m + mappingConfidence * 0.3m + passRate * 0.2m, 2);
        result.Outcome = result.HardStopCount == 0 && result.ReferCount == 0 && result.Confidence >= 80m
            ? "AutoQuote" : "Referred";
        return result;
    }
}
