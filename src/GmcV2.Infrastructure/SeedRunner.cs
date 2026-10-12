using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Idempotent reference-data seeding at the repository level, so it works for
/// BOTH InMemoryRepository and SqlRepository.
///
/// NOTE (mirrored, not referenced): the default PricingMasterVersion and the
/// guardrail rule set here are deliberate local duplicates of
/// Knowledge.GuardrailRules.StandardSet() and Core Pricing.cs defaults.
/// GmcV2.Infrastructure must not depend on GmcV2.Knowledge; when the KNOW
/// agent changes the standard set, update both copies and sql/DDL/09_seed.sql.
/// </summary>
public static class SeedRunner
{
    public static async Task SeedReferenceDataAsync(IGmcRepository repo)
    {
        await EnsureActiveMasterVersionAsync(repo);
        await EnsureGuardrailRulesAsync(repo);
        await EnsurePaidStatusMapAsync(repo);
    }

    // ─────────────────── pricing master (mirrors Pricing.cs defaults) ───────────────────

    private static async Task EnsureActiveMasterVersionAsync(IGmcRepository repo)
    {
        PricingMasterVersion active;
        try
        {
            active = await repo.GetActiveMasterVersionAsync();
            if (active is not null) return;
        }
        catch (InvalidOperationException)
        {
            // no active version — fall through and create the seed
        }

        var seed = new PricingMasterVersion
        {
            VersionRef = "PMV-2026Q4-SEED",
            Status = "Active",
            EffectiveFromUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        await repo.AddMasterVersionAsync(seed);
    }

    // ─────────────────── guardrails (mirrors GuardrailRules.StandardSet) ───────────────────

    public static List<GuardrailRuleDef> StandardGuardrailSet() => new()
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

    private static async Task EnsureGuardrailRulesAsync(IGmcRepository repo)
    {
        var existing = await repo.GetGuardrailRulesAsync();
        if (existing.Count > 0) return;
        foreach (var rule in StandardGuardrailSet())
            await repo.UpsertGuardrailRuleAsync(rule);
    }

    // ─────────────────── paid status map ───────────────────

    private static async Task EnsurePaidStatusMapAsync(IGmcRepository repo)
    {
        var existing = await repo.GetPaidStatusMapAsync();
        if (existing.Count > 0) return;

        // Repository-level seeding: InMemoryRepository has no paid-status writer in
        // IGmcRepository, so seed via the generic upsert path (SQL table already has
        // these rows from 09_seed.sql; the in-memory store builds them here).
        if (repo is InMemoryRepository mem)
            mem.SeedPaidStatus(DefaultPaidStatusPairs());
        // SqlRepository reads the rows seeded by sql/DDL/09_seed.sql.
    }

    internal static (string Raw, string Normalized)[] DefaultPaidStatusPairs() => new[]
    {
        ("Paid", "Paid"), ("Claim Paid", "Paid"), ("Payment Done", "Paid"),
        ("Settled", "Paid"), ("Claim Settled", "Paid"),
        ("Outstanding", "Outstanding"), ("Pending", "Outstanding"), ("In Process", "Outstanding"),
        ("Under Process", "Outstanding"), ("Open", "Outstanding"),
        ("Closed", "Closed"), ("Closed-No Claim", "Closed"), ("Withdrawn", "Closed"),
        ("Rejected", "Rejected"), ("Denied", "Rejected"), ("Declined", "Rejected"), ("Repudiated", "Rejected"),
    };
}
