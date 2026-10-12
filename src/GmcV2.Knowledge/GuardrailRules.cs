using GmcV2.Core;

namespace GmcV2.Knowledge;

/// <summary>
/// The standard guardrail rule set (db §10) as code — the same 15 rules the SQL
/// seed (09_seed.sql) and SeedRunner carry. Knowledge owns the canonical definition;
/// Infrastructure mirrors it because Infrastructure must not reference Knowledge.
/// </summary>
public static class GuardrailRules
{
    public static List<GuardrailRuleDef> StandardSet() => new()
    {
        new GuardrailRuleDef { RuleCode = "GRD-MIN-LIVES",          Category = "DataSufficiency", MetricSelector = "CoveredLives",          Operator = ">=", Threshold = 300m,  Severity = "HardStop", MessageTemplate = "Only {observed} covered lives; at least 300 required for credible pricing.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-MIN-CLAIMS",         Category = "DataSufficiency", MetricSelector = "ClaimCount",            Operator = ">=", Threshold = 50m,   Severity = "HardStop", MessageTemplate = "Only {observed} claims in the experience period; at least 50 required.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-DQ-SCORE",           Category = "DataSufficiency", MetricSelector = "DataQualityScore",      Operator = ">=", Threshold = 90m,   Severity = "HardStop", MessageTemplate = "Data quality score {observed} is below the floor of 90.", AppliesToExperienceBusiness = false },
        new GuardrailRuleDef { RuleCode = "GRD-MAPPING-CONF",       Category = "DataSufficiency", MetricSelector = "MappingConfidenceAvg",  Operator = ">=", Threshold = 90m,   Severity = "HardStop", MessageTemplate = "Average mapping confidence {observed} is below 90.", AppliesToExperienceBusiness = false },
        new GuardrailRuleDef { RuleCode = "GRD-VOLATILITY",         Category = "Volatility",      MetricSelector = "VolatilityCv",          Operator = "<=", Threshold = 2.5m,  Severity = "Refer",    MessageTemplate = "Claim size volatility CV {observed} exceeds 2.5.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-TOP5",               Category = "Volatility",      MetricSelector = "Top5ClaimSharePct",     Operator = "<=", Threshold = 45m,   Severity = "Refer",    MessageTemplate = "Top 5 claims are {observed}% of incurred cost, above the 45% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-LARGE-CLAIM",        Category = "Volatility",      MetricSelector = "LargeClaimSharePct",    Operator = "<=", Threshold = 35m,   Severity = "Refer",    MessageTemplate = "Large claims (₹2L+) are {observed}% of cost, above the 35% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-SINGLE-LARGEST",     Category = "Volatility",      MetricSelector = "SingleLargestSharePct", Operator = "<=", Threshold = 20m,   Severity = "Refer",    MessageTemplate = "The single largest claim is {observed}% of cost, above the 20% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-AVG-AGE",            Category = "Demographic",     MetricSelector = "AvgAge",                Operator = "<=", Threshold = 48m,   Severity = "Refer",    MessageTemplate = "Average age {observed} exceeds 48.", AppliesToExperienceBusiness = false },
        new GuardrailRuleDef { RuleCode = "GRD-SENIOR-SHARE",       Category = "Demographic",     MetricSelector = "SeniorAgeSharePct",     Operator = "<=", Threshold = 30m,   Severity = "Refer",    MessageTemplate = "Members aged 55+ are {observed}% of lives, above the 30% cap.", AppliesToExperienceBusiness = false },
        new GuardrailRuleDef { RuleCode = "GRD-PARENT-SHARE",       Category = "Demographic",     MetricSelector = "ParentSharePct",        Operator = "<=", Threshold = 20m,   Severity = "Refer",    MessageTemplate = "Parents are {observed}% of lives, above the 20% cap.", AppliesToExperienceBusiness = false },
        new GuardrailRuleDef { RuleCode = "GRD-CHRONIC-PREVALENCE", Category = "Clinical",        MetricSelector = "ChronicPrevalencePct",  Operator = "<=", Threshold = 40m,   Severity = "Refer",    MessageTemplate = "Chronic prevalence {observed}% exceeds the 40% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-MATERNITY-SHARE",    Category = "Clinical",        MetricSelector = "MaternitySharePct",     Operator = "<=", Threshold = 30m,   Severity = "Refer",    MessageTemplate = "Maternity is {observed}% of cost, above the 30% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-NON-NETWORK",        Category = "Commercial",      MetricSelector = "NonNetworkSharePct",    Operator = "<=", Threshold = 25m,   Severity = "Refer",    MessageTemplate = "Non-network claims are {observed}% of cost, above the 25% cap.", AppliesToExperienceBusiness = true },
        new GuardrailRuleDef { RuleCode = "GRD-RATE-PER-LIFE",      Category = "Commercial",      MetricSelector = "RatePerLifeBeforeGst",  Operator = ">=", Threshold = 5000m, Severity = "Refer",    MessageTemplate = "Technical rate per life ₹{observed} is below the ₹5,000 floor.", AppliesToExperienceBusiness = false },
    };
}
