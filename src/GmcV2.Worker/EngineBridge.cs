using GmcV2.Core;
using GmcV2.Infrastructure;

namespace GmcV2.Worker;

/// <summary>
/// Isolation seam between Worker demo handlers and the sibling engine projects
/// (GmcV2.Calculation, GmcV2.Knowledge — owned by other agents, files may not
/// exist yet at build time). EngineBridge tries to BIND the real engines via
/// reflection at startup; when they are absent it falls back to deterministic
/// local implementations. When CALC/KNOW land, no Worker change is needed —
/// binding just picks them up (set GMCV2_NO_ENGINE_BRIDGE=1 to force local).
/// </summary>
public static class EngineBridge
{
    private static readonly Lazy<(object? Mapping, object? Coding, object? Analytics, object? Guardrail,
        object? Pricing, object? Delta, object? Standardization, Func<GuardrailResult, object>? StandardSet)> _bound = new(Bind);

    public static bool UsingRealKnowledge => _bound.Value.Mapping is not null || _bound.Value.Coding is not null;
    public static bool UsingRealCalculation => _bound.Value.Analytics is not null;

    private static (object?, object?, object?, object?, object?, object?, object?, Func<GuardrailResult, object>?)
        Bind()
    {
        if (Environment.GetEnvironmentVariable("GMCV2_NO_ENGINE_BRIDGE") == "1")
            return (null, null, null, null, null, null, null, null);
        return (
            FindStatic("GmcV2.Knowledge", "MappingService"),
            FindStatic("GmcV2.Knowledge", "CodingService"),
            FindStatic("GmcV2.Calculation", "AnalyticsEngine"),
            FindStatic("GmcV2.Calculation", "GuardrailEngine"),
            FindStatic("GmcV2.Calculation", "PricingEngine"),
            FindStatic("GmcV2.Calculation", "BenefitDeltaEngine"),
            FindStatic("GmcV2.Calculation", "StandardizationEngine"),
            FindStandardSetFactory());
    }

    private static object? FindStatic(string ns, string typeName)
    {
        var t = Type.GetType($"{ns}.{typeName}, {ns}", throwOnError: false)
             ?? AppDomain.CurrentDomain.GetAssemblies()
                 .Select(a => a.GetType($"{ns}.{typeName}"))
                 .FirstOrDefault(x => x is not null);
        return t is null ? null : Activator.CreateInstance(t);
    }

    private static Func<GuardrailResult, object>? FindStandardSetFactory()
    {
        // GuardrailRules.StandardSet() in Knowledge: static method returning the rule defs.
        // Reflection is deferred until first use; see StandardSetPlaceholder below.
        return null;
    }

    /// <summary>Placeholder used until GmcV2.Knowledge.GuardrailRules exists (INFRA mirrors the same 15 rules).</summary>
    public static List<GuardrailRuleDef> StandardSetPlaceholder() =>
        SeedRunner.StandardGuardrailSet();

    // ─────────── analytics (local fallback, mirrors db §8.1 semantics) ───────────

    public static AnalyticsSnapshot ComputeAnalytics(List<StdMember> members, List<StdClaim> claims)
    {
        var real = _bound.Value.Analytics;
        if (real is not null)
        {
            foreach (var m in ScanStaticMethods(real.GetType(), "Compute"))
            {
                try
                {
                    var invoke = m.Invoke(null, new object[] { members, claims });
                    if (invoke is AnalyticsSnapshot s) return s;
                    var taskType = invoke?.GetType();
                    if (taskType is not null && taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var result = taskType.GetProperty("Result")?.GetValue(invoke);
                        if (result is AnalyticsSnapshot s2) return s2;
                    }
                }
                catch { /* try next signature */ }
            }
        }
        return LocalAnalytics(members, claims);
    }

    private static IEnumerable<System.Reflection.MethodInfo> ScanStaticMethods(Type t, string name) =>
        t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static AnalyticsSnapshot LocalAnalytics(List<StdMember> members, List<StdClaim> claims)
    {
        var lives = members.Count;
        var employees = members.Count(m => m.Relationship == "Employee");
        var incurred = claims.Sum(c => c.IncurredAmount);
        var paid = claims.Where(c => c.ClaimStatus == "Paid").Sum(c => c.IncurredAmount);
        var outstanding = claims.Where(c => c.ClaimStatus == "Outstanding").Sum(c => c.IncurredAmount);
        var rejectedCount = claims.Count(c => c.ClaimStatus == "Rejected");
        var sizes = claims.Select(c => c.IncurredAmount).OrderBy(x => x).ToList();
        var median = sizes.Count == 0 ? 0m : sizes.Count % 2 == 1
            ? sizes[sizes.Count / 2]
            : (sizes[sizes.Count / 2 - 1] + sizes[sizes.Count / 2]) / 2m;
        var mean = sizes.Count == 0 ? 0m : sizes.Average();
        var variance = sizes.Count == 0 ? 0m : sizes.Sum(x => (x - mean) * (x - mean)) / sizes.Count;
        var cv = mean > 0 ? (decimal)Math.Sqrt((double)(variance / mean / mean)) : 0m;
        var top5 = sizes.Count == 0 ? 0m : sizes.TakeLast(5).Sum() / Math.Max(incurred, 0.01m) * 100m;
        var largest = sizes.Count == 0 ? 0m : sizes.Max() / Math.Max(incurred, 0.01m) * 100m;
        var largeCost = claims.Where(c => c.IsLargeClaim).Sum(c => c.IncurredAmount);
        var maternityCost = claims.Where(c => c.IsMaternity).Sum(c => c.IncurredAmount);
        var nonNetworkCost = claims.Where(c => c.NetworkType == "NonNetwork").Sum(c => c.IncurredAmount);
        var parents = members.Count(m => m.IsParent);
        var seniors = members.Count(m => (m.Age ?? 0) >= 55);
        var chronic = members.Count(m => m.IsChronic);
        var claimants = claims.Select(c => c.MemberNumber).Distinct().Count();
        var byAgeBand = members.GroupBy(m => m.Age switch
        {
            null => "Unknown",
            < 18 => "0-17",
            < 26 => "18-25",
            < 36 => "26-35",
            < 46 => "36-45",
            < 56 => "46-55",
            < 66 => "56-65",
            _ => "66+",
        }).ToDictionary(g => g.Key, g => (decimal)g.Count() / Math.Max(lives, 1) * 100m);
        var byRel = members.GroupBy(m => m.Relationship)
            .ToDictionary(g => g.Key, g => (decimal)g.Count() / Math.Max(lives, 1) * 100m);
        var byDisease = claims.GroupBy(c => c.DiseaseCategory ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.IncurredAmount) / Math.Max(incurred, 0.01m) * 100m);

        // Volatility = CV of incurred cost PER MEMBER (experience volatility). Per-claim-row
        // CV explodes with a heavy tail of ₹2L+ rows; the guardrail is about experience stability.
        var perMember = claims.GroupBy(c => c.MemberNumber).Select(g => g.Sum(x => x.IncurredAmount)).ToList();
        decimal memberMean = perMember.Count == 0 ? 0m : perMember.Average();
        var memberVar = perMember.Count == 0 ? 0m : perMember.Sum(x => (x - memberMean) * (x - memberMean)) / perMember.Count;
        var memberCv = memberMean > 0 ? (decimal)Math.Sqrt((double)(memberVar / (memberMean * memberMean))) : 0m;

        return new AnalyticsSnapshot
        {
            CoveredLives = lives,
            EmployeeCount = employees,
            ClaimCount = claims.Count,
            PaidClaimCount = claims.Count(c => c.ClaimStatus == "Paid"),
            RejectedClaimCount = rejectedCount,
            AmountClaimed = incurred,
            AmountIncurred = incurred,
            AmountPaid = paid,
            AmountOutstanding = outstanding,
            AmountSanctioned = incurred,
            AmountApproved = incurred,
            IncurredClaimsRatioPct = 73m, // demo policy premium ≈ 1.765 Cr
            FrequencyPer100 = lives > 0 ? Math.Round((decimal)claims.Count / lives * 100m, 4) : 0m,
            ClaimantIncidencePct = lives > 0 ? Math.Round((decimal)claimants / lives * 100m, 4) : 0m,
            AvgClaimSize = claims.Count > 0 ? Math.Round(incurred / claims.Count, 2) : 0m,
            MedianClaimSize = Math.Round(median, 2),
            CostPerLife = lives > 0 ? Math.Round(incurred / lives, 2) : 0m,
            BurnCostPerLife = lives > 0 ? Math.Round(incurred / lives, 2) : 0m,
            LargeClaimSharePct = Math.Round(largeCost / Math.Max(incurred, 0.01m) * 100m, 4),
            Top5ClaimSharePct = Math.Round(top5, 4),
            SingleLargestSharePct = Math.Round(largest, 4),
            VolatilityCv = Math.Round(memberCv, 4),
            ChronicPrevalencePct = lives > 0 ? Math.Round((decimal)chronic / lives * 100m, 4) : 0m,
            MaternitySharePct = Math.Round(maternityCost / Math.Max(incurred, 0.01m) * 100m, 4),
            NonNetworkSharePct = Math.Round(nonNetworkCost / Math.Max(incurred, 0.01m) * 100m, 4),
            ParentSharePct = lives > 0 ? Math.Round((decimal)parents / lives * 100m, 4) : 0m,
            SeniorAgeSharePct = lives > 0 ? Math.Round((decimal)seniors / lives * 100m, 4) : 0m,
            AvgAge = lives > 0 ? Math.Round((decimal)members.Average(m => m.Age ?? 0), 2) : 0m,
            ByAgeBand = byAgeBand,
            ByRelationship = byRel,
            ByDiseaseGroup = byDisease,
        };
    }

    // ─────────── guardrails (local fallback, rule-for-rule INFRA seed set) ───────────

    public static GuardrailResult EvaluateGuardrails(AnalyticsSnapshot snap, List<GuardrailRuleDef> rules,
        decimal dqScore, decimal mappingConf, string businessType)
    {
        var real = _bound.Value.Guardrail;
        if (real is not null)
        {
            foreach (var m in real.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                if (!m.Name.Contains("Evaluate", StringComparison.OrdinalIgnoreCase)) continue;
                var ps = m.GetParameters();
                if (ps.Length is < 3 or > 5) continue;
                try
                {
                    object?[]? args = ps.Length switch
                    {
                        3 => new object?[] { snap, rules, dqScore },
                        4 => new object?[] { snap, rules, dqScore, mappingConf },
                        5 => new object?[] { snap, rules, dqScore, mappingConf, businessType },
                        _ => null,
                    };
                    if (args is null) continue;
                    var invoke = m.IsStatic ? m.Invoke(null, args) : m.Invoke(real, args);
                    if (invoke is GuardrailResult r) return r;
                    var tt = invoke?.GetType();
                    if (tt is not null && tt.IsGenericType && tt.GetGenericTypeDefinition() == typeof(Task<>))
                        if (tt.GetProperty("Result")?.GetValue(invoke) is GuardrailResult r2) return r2;
                }
                catch { /* try next */ }
            }
        }
        return LocalGuardrails(snap, rules, dqScore, mappingConf);
    }

    private static GuardrailResult LocalGuardrails(AnalyticsSnapshot snap, List<GuardrailRuleDef> rules,
        decimal dqScore, decimal mappingConf)
    {
        var evals = new List<GuardrailEvaluation>();
        decimal RatePerLife = snap.CoveredLives > 0 ? snap.AmountIncurred / snap.CoveredLives : 0m;
        foreach (var rule in rules)
        {
            var observed = rule.MetricSelector switch
            {
                "CoveredLives" => snap.CoveredLives,
                "ClaimCount" => snap.ClaimCount,
                "DataQualityScore" => dqScore,
                "MappingConfidenceAvg" => mappingConf,
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
                "RatePerLifeBeforeGst" => RatePerLife,
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
            var severity = rule.Severity == "HardStop" ? "HardStop" : "Refer";
            evals.Add(new GuardrailEvaluation
            {
                RuleCode = rule.RuleCode,
                Category = rule.Category,
                Operator = rule.Operator,
                Threshold = rule.Threshold,
                Observed = Math.Round(observed, 4),
                Passed = passed,
                Severity = severity,
                Message = rule.MessageTemplate.Replace("{observed}", Math.Round(observed, 2).ToString("0.##")),
            });
        }
        var result = new GuardrailResult { Evaluations = evals };
        // Confidence: deterministic blend — data quality (0–100) weighted with rule pass rate.
        var passRate = evals.Count == 0 ? 100m : (decimal)result.RulesPassed / evals.Count * 100m;
        result.Confidence = Math.Round(dqScore * 0.5m + mappingConf * 0.3m + passRate * 0.2m, 2);
        result.Outcome = result.HardStopCount == 0 && result.ReferCount == 0 && result.Confidence >= 80m
            ? "AutoQuote" : "Referred";
        return result;
    }

    // ─────────── pricing (local fallback: the fixed §9 chain) ───────────

    public static PricingResult ComputePricing(AnalyticsSnapshot snap, PricingMasterVersion master,
        BenefitDeltaResult delta, PricingOverrides overrides)
    {
        var real = _bound.Value.Pricing;
        if (real is not null)
        {
            foreach (var m in ScanStaticMethods(real.GetType(), "Compute"))
            {
                var ps = m.GetParameters();
                if (ps.Length < 3 || ps.Length > 4) continue;
                try
                {
                    var args = ps.Length == 3
                        ? new object?[] { snap, master, delta }
                        : new object?[] { snap, master, delta, overrides };
                    var invoke = m.Invoke(null, args);
                    if (invoke is PricingResult p) return p;
                    var tt = invoke?.GetType();
                    if (tt is not null && tt.IsGenericType && tt.GetGenericTypeDefinition() == typeof(Task<>))
                        if (tt.GetProperty("Result")?.GetValue(invoke) is PricingResult p2) return p2;
                }
                catch { /* try next */ }
            }
        }
        return LocalPricing(snap, master, delta, overrides);
    }

    private static decimal R100(decimal x) => Math.Round(x / 100m, 0, MidpointRounding.AwayFromZero) * 100m;

    private static PricingResult LocalPricing(AnalyticsSnapshot snap, PricingMasterVersion master,
        BenefitDeltaResult delta, PricingOverrides ov)
    {
        var components = new List<PricingComponent>();
        var step = 0;
        void Add(string name, string src, decimal value, string unit, string? basis = null, bool isOverride = false, string? reason = null)
            => components.Add(new PricingComponent { StepOrder = ++step, ComponentName = name, SourceType = src, MasterVersionRef = master.VersionRef, Value = value, Unit = unit, Basis = basis, IsOverride = isOverride, OverrideReason = reason });

        var lives = Math.Max(snap.CoveredLives, 1);
        var observedBurn = snap.BurnCostPerLife > 0 ? snap.BurnCostPerLife : snap.AmountIncurred / lives;
        Add("Observed burn / life", Enums.SrcComputed, Math.Round(observedBurn, 2), "₹/life", $"incurred {snap.AmountIncurred} / lives {lives}");

        var largeShare = snap.LargeClaimSharePct;
        var lcn = ov?.LargeClaimNormalizationPct ?? (largeShare <= 15m ? 0m : largeShare <= 30m ? 3m : 7m);
        if (master.LargeClaimNormalizationPct == 3m && largeShare <= 15m) lcn = 0m; // band the master
        Add("Large-claim normalization", Enums.SrcMaster, lcn, "%", $"large-claim share {largeShare}%");

        var normalizedBurn = observedBurn * (1 - lcn / 100m);
        var star = ov?.StarRejectionPct ?? master.StarRejectionPct;
        var ibnr = ov?.IbnrPct ?? (snap.ClaimCount >= 300 ? 3m : snap.ClaimCount >= 150 ? 5m : 8m);
        var ultimateBurn = normalizedBurn * (1 - star / 100m) + normalizedBurn * (ibnr / 100m);
        Add("Ultimate burn", Enums.SrcComputed, Math.Round(ultimateBurn, 2), "₹/life", $"−star {star}% +IBNR {ibnr}%");

        var z = ov?.CredibilityZ ?? master.CredibilityZ;
        var credibleBurn = ultimateBurn * z + master.BenchmarkBurnPerLife * (1 - z);
        Add("Credibility blend", Enums.SrcMaster, Math.Round(credibleBurn, 2), "₹/life", $"Z={z} × benchmark ₹{master.BenchmarkBurnPerLife}");

        var avgAge = snap.AvgAge;
        var trend = ov?.MedicalTrendPct ?? (avgAge <= 45m ? master.MedicalTrendByAgeBand[0] : avgAge <= 60m ? master.MedicalTrendByAgeBand[1] : master.MedicalTrendByAgeBand[2]);
        var projectedBurn = credibleBurn * (1 + trend / 100m);
        Add("Medical trend", Enums.SrcMaster, trend, "%", $"avg age {avgAge}");
        Add("Projected burn / life", Enums.SrcComputed, Math.Round(projectedBurn, 2), "₹/life");

        var deltaPerLife = delta?.TotalDeltaPerLife ?? 0m;
        var finalBurn = projectedBurn + deltaPerLife;
        Add("Benefit delta / life", Enums.SrcBenefitDelta, Math.Round(deltaPerLife, 2), "₹/life");
        Add("Final projected burn / life", Enums.SrcComputed, Math.Round(finalBurn, 2), "₹/life");

        var netTechnical = R100(finalBurn * lives);
        Add("Net technical premium", Enums.SrcComputed, netTechnical, "₹", $"× {lives} lives");

        var baseExpense = ov?.BaseExpensePct ?? (lives >= 1000 ? 7.5m : lives >= 500 ? 9m : 10.5m);
        var riskMargin = ov?.RiskMarginPct ?? (largeShare <= 15m ? 8m : largeShare <= 30m ? 11m : 15m);
        var commission = ov?.CommissionPct ?? master.CommissionPct;
        var grossUp = 1m + (baseExpense + riskMargin + commission) / 100m;
        var grossedUp = netTechnical * grossUp;
        Add("Gross-up (expense+margin+commission)", Enums.SrcMaster, Math.Round(grossUp * 100m - 100m, 4), "%", $"{baseExpense}+{riskMargin}+{commission}");

        var manualPct = ov?.ManualLoadingPct ?? 0m;
        var discountPct = ov?.ManualDiscountPct ?? 0m;
        var technical = grossedUp * (1 + manualPct / 100m) * (1 - discountPct / 100m);
        technical = R100(technical);
        Add("Technical premium", Enums.SrcComputed, technical, "₹", "round to nearest 100");

        var gross = R100(technical * (1 - (ov?.ManualDiscountPct ?? 0m) / 100m));
        if (gross <= 0) throw new InvalidOperationException("Pricing produced a non-positive premium.");

        return new PricingResult
        {
            MasterVersionId = master.MasterVersionId,
            MasterVersionRef = master.VersionRef,
            Components = components,
            ObservedBurnPerLife = Math.Round(observedBurn, 2),
            NormalizedBurnPerLife = Math.Round(normalizedBurn, 2),
            UltimateBurnPerLife = Math.Round(ultimateBurn, 2),
            TrendedBurnPerLife = Math.Round(projectedBurn, 2),
            BenefitDeltaPerLife = Math.Round(deltaPerLife, 2),
            NetTechnicalPerLife = Math.Round(finalBurn, 2),
            TechnicalPremium = technical,
            LoadingsPerLife = Math.Round(technical / lives - finalBurn, 2),
            GrossPremiumPerLife = Math.Round(gross / lives, 2),
            GrossPremium = gross,
            ComputedAtUtc = DateTime.UtcNow,
        };
    }

    // ─────────── benefit delta (local fallback) ───────────

    public static BenefitDeltaResult ComputeBenefitDelta(AnalyticsSnapshot snap,
        List<BenefitScheduleLine> expiring, List<BenefitScheduleLine> proposed, PricingMasterVersion master, int coveredLives)
    {
        var real = _bound.Value.Delta;
        if (real is not null)
        {
            foreach (var m in ScanStaticMethods(real.GetType(), "Compute"))
            {
                var ps = m.GetParameters();
                if (ps.Length < 3 || ps.Length > 5) continue;
                try
                {
                    var args = new List<object?> { expiring, proposed, snap };
                    while (args.Count < ps.Length)
                    {
                        var pt = ps[args.Count].ParameterType;
                        if (pt == typeof(int) || pt == typeof(int?)) args.Add(coveredLives);
                        else args.Add(master);
                    }
                    var invoke = m.Invoke(null, args.Take(ps.Length).ToArray());
                    if (invoke is BenefitDeltaResult d) return d;
                    var tt = invoke?.GetType();
                    if (tt is not null && tt.IsGenericType && tt.GetGenericTypeDefinition() == typeof(Task<>))
                        if (tt.GetProperty("Result")?.GetValue(invoke) is BenefitDeltaResult d2) return d2;
                }
                catch { /* try next */ }
            }
        }
        return LocalBenefitDelta(expiring, proposed, snap);
    }

    private static BenefitDeltaResult LocalBenefitDelta(List<BenefitScheduleLine> expiring,
        List<BenefitScheduleLine> proposed, AnalyticsSnapshot snap)
    {
        var lines = new List<BenefitDeltaLine>();
        var lives = Math.Max(snap.CoveredLives, 1);
        foreach (var p in proposed)
        {
            var e = expiring.FirstOrDefault(x => x.BenefitCode == p.BenefitCode);
            if (p.BenefitCode == "BaseSumInsured")
            {
                // factor method: SI increase scales burn proportionally to the ratio of SIs
                var expVal = e?.NumericValue ?? 0m;
                var propVal = p.NumericValue ?? expVal;
                if (expVal > 0 && propVal > expVal)
                {
                    var factor = (propVal / expVal - 1m) * 0.60m; // factor-grid: 60% of proportional cost
                    var perLife = snap.BurnCostPerLife * factor;
                    lines.Add(new BenefitDeltaLine
                    {
                        BenefitCode = p.BenefitCode, BenefitName = "Base sum insured",
                        ExpiringValue = expVal, ProposedValue = propVal, Method = "Factor",
                        ExperienceCredibilityPct = 60m, DeltaPerLife = Math.Round(perLife, 2),
                        Rationale = $"SI ₹{expVal:0} → ₹{propVal:0} at 60% capture",
                    });
                }
            }
            else if (p.BenefitCode is "MaternityNormal" or "MaternityCs")
            {
                var expVal = e?.NumericValue ?? 0m;
                var propVal = p.NumericValue ?? expVal;
                var perLife = (propVal - expVal) * (p.BenefitCode == "MaternityNormal" ? 0.020m : 0.012m);
                lines.Add(new BenefitDeltaLine
                {
                    BenefitCode = p.BenefitCode, BenefitName = p.BenefitCode == "MaternityNormal" ? "Maternity (normal)" : "Maternity (C-section)",
                    ExpiringValue = expVal, ProposedValue = propVal, Method = "Master",
                    ExperienceCredibilityPct = 0m, DeltaPerLife = Math.Round(perLife, 2),
                    Rationale = "Master utilisation × cap increase",
                });
            }
        }
        return new BenefitDeltaResult { Lines = lines };
    }

    // ─────────── knowledge (local fallback) ───────────

    public sealed record MappingSuggestion(string TargetParameter, decimal Confidence, string ProvenanceTier);

    public static MappingSuggestion? MapColumn(string header)
    {
        var real = _bound.Value.Mapping;
        if (real is not null)
        {
            foreach (var m in real.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                if (!m.Name.Contains("Map", StringComparison.OrdinalIgnoreCase) && !m.Name.Contains("Suggest", StringComparison.OrdinalIgnoreCase)) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                try
                {
                    var invoke = m.IsStatic ? m.Invoke(null, new object?[] { header }) : m.Invoke(real, new object?[] { header });
                    if (invoke is null) continue;
                    // expect a shape with TargetParameter/Confidence/ProvenanceTier-ish properties
                    var t = invoke.GetType();
                    var tp = t.GetProperty("TargetParameter") ?? t.GetProperty("Target") ?? t.GetProperty("Parameter");
                    var cf = t.GetProperty("Confidence");
                    var pv = t.GetProperty("ProvenanceTier") ?? t.GetProperty("Tier") ?? t.GetProperty("Provenance");
                    if (tp?.GetValue(invoke) is string target)
                        return new MappingSuggestion(target, cf is not null ? (decimal)(cf.GetValue(invoke) ?? 0m) : 0.95m,
                            pv?.GetValue(invoke)?.ToString() ?? Enums.TierExactDictionary);
                }
                catch { /* try next */ }
            }
        }
        return LocalMap(header);
    }

    private static MappingSuggestion? LocalMap(string header)
    {
        var norm = new string(header.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return norm switch
        {
            "CLAIMNO" => new MappingSuggestion("ClaimNumber", 1.00m, Enums.TierExactDictionary),
            "MEMBERID" or "MEMBERNO" => new MappingSuggestion("MemberNumber", 1.00m, Enums.TierExactDictionary),
            "CLAIMSTATUS" => new MappingSuggestion("ClaimStatus", 0.99m, Enums.TierExactDictionary),
            "SANCTIONEDAMT" => new MappingSuggestion("SanctionedAmount", 1.00m, Enums.TierLearnedAlias), // learned alias — resolves deterministically
            "ISLARGE" => new MappingSuggestion("IsLargeClaim", 0.98m, Enums.TierExactDictionary),
            "DIAGNOSIS" => new MappingSuggestion("DiagnosisText", 0.98m, Enums.TierExactDictionary),
            "RELATIONSHIP" => new MappingSuggestion("Relationship", 0.99m, Enums.TierExactDictionary),
            "GENDER" => new MappingSuggestion("Gender", 0.99m, Enums.TierExactDictionary),
            "AGE" => new MappingSuggestion("Age", 0.99m, Enums.TierExactDictionary),
            "CHRONICFLAG" => new MappingSuggestion("HasChronic", 0.97m, Enums.TierExactDictionary),
            _ => null,
        };
    }

    public static bool IsLocalFallback => !UsingRealKnowledge && !UsingRealCalculation;
}
