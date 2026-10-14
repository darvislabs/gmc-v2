using System.Text;
using System.Text.Json;
using GmcV2.Core;
using GmcV2.Infrastructure;

namespace GmcV2.Worker;

/// <summary>
/// Demo stage handlers (ABC Tech dataset, seeded Random(42), deterministic).
/// All calculation/knowledge calls go through EngineBridge, which binds the
/// real GmcV2.Calculation / GmcV2.Knowledge engines when those projects are
/// built and falls back to deterministic local implementations otherwise.
/// </summary>
public static class StageHandlers
{
    public static IStageHandler[] Demo() => new IStageHandler[]
    {
        new DemoIntakeHandler(),
        new DemoProfileHandler(),
        new DemoExtractDocumentsHandler(),
        new DemoMapHandler(),
        new DemoStandardizeHandler(),
        new DemoCodeClinicalHandler(),
        new DemoAnalyzeHandler(),
        new DemoGuardrailHandler(),
        new DemoPriceHandler(),
        new DemoCostBenefitsHandler(),
        new DemoCompareCompetitionHandler(),
        new DemoAssembleQuoteHandler(),
        new DemoPublishHandler(),
    };

    public static IStageHandler[] Stub() => Enums.Stages.Select(s => (IStageHandler)new StubHandler(s)).ToArray();

    // ───────────────────────── ABC Tech demo dataset ─────────────────────────
    // Pinned by the smoke tests: 1,240 lives (825 employees), 4,180 claim rows,
    // incurred ≈ 1,240 × 10,390 = ₹1,28,83,600, chronic ≈ 21%, large-claim COST
    // share ≈ 18% (< 35), senior share ≈ 12% (< 30), parent share ≈ 15% (< 20),
    // avg age ≤ 48, DQ 96, mapping confidence 99.

    internal const int DemoLives = 1240;
    internal const int DemoEmployees = 825;
    internal const int DemoClaimRows = 4180;
    internal const decimal DemoIncurredPerLife = 10390m;

    public sealed class DemoData
    {
        public List<(string MemberNumber, string Relationship, string Gender, int Age, bool Chronic, bool Parent)> Members { get; } = new();
        public List<(string ClaimNumber, string MemberNumber, string Status, decimal Incurred, bool Large, bool Maternity, bool NonNetwork, string Disease)> Claims { get; } = new();
        public decimal DataQualityScore { get; } = 96m;
        public decimal MappingConfidence { get; } = 99m;
        public List<BenefitScheduleLine> Expiring { get; } = DemoBenefitSchedule("Expiring", 300000m);
        public List<BenefitScheduleLine> Proposed { get; } = DemoBenefitSchedule("Proposed", 500000m);
    }

    internal static DemoData BuildDemoData()
    {
        var data = new DemoData();
        var rng = new Random(42);

        // ── members: 1240 rows; employees 24–45; spouses 30–49 (+34 seniors);
        //    children 3–20; parents 58–72 (15% of lives); chronic ≈ 21% overall;
        //    seniors ≈ 17% (< 30 cap); avg age ≈ 40 (≤ 48 cap).
        for (var i = 0; i < DemoEmployees; i++)
        {
            var isChronic = rng.NextDouble() < 0.15;
            data.Members.Add(($"EMP{i + 1:D5}", "Employee", i % 2 == 0 ? "Male" : "Female", 24 + rng.Next(0, 22), isChronic, false));
        }
        var dependentsNeeded = DemoLives - DemoEmployees;
        var parentsNeeded = 186;   // 15.0%
        var spouseCount = 220;     // incl. 34 senior spouses
        var childrenCount = dependentsNeeded - parentsNeeded - spouseCount;
        for (var i = 0; i < spouseCount; i++)
        {
            var age = i < 34 ? 56 + rng.Next(0, 6) : 30 + rng.Next(0, 20);
            data.Members.Add(($"DEP{i + 1:D5}", "Spouse", "Female", age, rng.NextDouble() < 0.15, false));
        }
        for (var i = 0; i < childrenCount; i++)
            data.Members.Add(($"CHD{i + 1:D5}", "Child", i % 2 == 0 ? "Male" : "Female", 3 + rng.Next(0, 18), false, false));
        for (var i = 0; i < parentsNeeded; i++)
            data.Members.Add(($"PAR{i + 1:D5}", "Parent", i % 2 == 0 ? "Male" : "Female", 58 + rng.Next(0, 15), rng.NextDouble() < 0.55, true));

        // ── claims: exact total incurred = 1,240 × 10,390
        var totalIncurred = DemoIncurredPerLife * DemoLives; // 12,883,600
        var memberCount = data.Members.Count;
        var largeRows = 11;        // 11 rows ≥ ₹2L ⇒ ≈18% of cost
        var maternityRows = 150;   // ≈8% of cost
        var smallRows = DemoClaimRows - largeRows - maternityRows;

        var diseases = new[]
        {
            "Type 2 Diabetes Mellitus", "Essential (Primary) Hypertension", "Atherosclerotic Heart Disease",
            "Normal Delivery", "Caesarean Delivery", "Acute Appendicitis", "Senile Cataract",
            "Calculus of Kidney", "Pneumonia",
        };

        var largeBudget = totalIncurred * 0.18m;   // 2,319,048
        var maternityBudget = totalIncurred * 0.08m; // 1,030,688
        var smallBudget = totalIncurred - largeBudget - maternityBudget; // 9,533,864

        var claimIdx = 0;
        var nonNetworkTick = 0;
        for (var i = 0; i < largeRows; i++)
        {
            var m = data.Members[rng.Next(memberCount)];
            var amt = Math.Round(largeBudget / largeRows, 2); // ≈ 210,822 ≥ 2,00,000
            data.Claims.Add(($"CL{++claimIdx:D6}", m.MemberNumber, "Paid", amt, true, false, ++nonNetworkTick % 8 == 0, diseases[rng.Next(6)]));
        }
        var spouses = data.Members.Where(m => m.Relationship == "Spouse" || m.Relationship == "Employee").ToList();
        for (var i = 0; i < maternityRows; i++)
        {
            var m = spouses[rng.Next(spouses.Count)];
            var amt = Math.Round(maternityBudget / maternityRows, 2);
            data.Claims.Add(($"CL{++claimIdx:D6}", m.MemberNumber, i % 6 == 0 ? "Outstanding" : "Paid", amt, false, true, ++nonNetworkTick % 9 == 0, i % 2 == 0 ? "Normal Delivery" : "Caesarean Delivery"));
        }
        var perSmall = smallBudget / smallRows;
        for (var i = 0; i < smallRows; i++)
        {
            var m = data.Members[rng.Next(memberCount)];
            // deterministic jitter, last row absorbs the rounding drift
            var amt = i == smallRows - 1
                ? smallBudget - perSmall * (smallRows - 1)
                : Math.Round(perSmall * (0.6m + (decimal)((i * 37) % 90) / 100m), 2);
            var status = i % 11 == 0 ? "Rejected" : i % 5 == 0 ? "Outstanding" : "Paid";
            data.Claims.Add(($"CL{++claimIdx:D6}", m.MemberNumber, status, amt, false, false, ++nonNetworkTick % 8 == 0, diseases[rng.Next(6)]));
        }

        // exact reconciliation
        var drift = totalIncurred - data.Claims.Sum(c => c.Incurred);
        if (drift != 0)
        {
            var last = data.Claims[^1];
            data.Claims[^1] = (last.ClaimNumber, last.MemberNumber, last.Status, last.Incurred + drift, last.Large, last.Maternity, last.NonNetwork, last.Disease);
        }
        return data;
    }

    internal static readonly string[] DemoClaimsHeaders =
        { "Claim No", "Member ID", "Claim Status", "Sanctioned Amt", "Is Large", "Diagnosis" };
    internal static readonly string[] DemoEnrollmentHeaders =
        { "Member ID", "Relationship", "Gender", "Age", "Chronic Flag" };

    internal static string MapHeaderToParameter(string header) => header.Trim().ToUpperInvariant() switch
    {
        "CLAIM NO" => "ClaimNumber",
        "MEMBER ID" => "MemberNumber",
        "CLAIM STATUS" => "ClaimStatus",
        "SANCTIONED AMT" => "SanctionedAmount",
        "IS LARGE" => "IsLargeClaim",
        "DIAGNOSIS" => "DiagnosisText",
        "RELATIONSHIP" => "Relationship",
        "GENDER" => "Gender",
        "AGE" => "Age",
        "CHRONIC FLAG" => "HasChronic",
        _ => "Unmapped",
    };

    internal static bool IsRequiredParameter(string p) =>
        p is "ClaimNumber" or "MemberNumber" or "ClaimStatus" or "SanctionedAmount" or "Relationship" or "Age";

    internal static List<BenefitScheduleLine> DemoBenefitSchedule(string type, decimal baseSi) => new()
    {
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "BaseSumInsured", NumericValue = baseSi, Confidence = 0.98m, Citation = "RFQ p1" },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "MaternityNormal", NumericValue = type == "Proposed" ? 75000m : 50000m, Confidence = 0.97m, Citation = "RFQ p2" },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "MaternityCs", NumericValue = type == "Proposed" ? 100000m : 75000m, Confidence = 0.97m, Citation = "RFQ p2" },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "Ambulance", NumericValue = 3000m, Confidence = 0.95m, Citation = "RFQ p3" },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "RoomRent", TextValue = "TwoPercent", Confidence = 0.96m, Citation = "RFQ p3" },
        new BenefitScheduleLine { ScheduleType = type, BenefitCode = "PedDay1", TextValue = type == "Proposed" ? "Yes" : "No", Confidence = 0.94m, Citation = "RFQ p4" },
    };

    private static List<StdMember> StdMembers(DemoData d) => d.Members.Select(m => new StdMember
    {
        MemberNumber = m.MemberNumber, Relationship = m.Relationship, Gender = m.Gender,
        Age = m.Age, IsChronic = m.Chronic,
    }).ToList();

    private static List<StdClaim> StdClaims(DemoData d) => d.Claims.Select(cl => new StdClaim
    {
        ClaimNumber = cl.ClaimNumber, MemberNumber = cl.MemberNumber, ClaimStatus = cl.Status,
        IncurredAmount = cl.Incurred, IsLargeClaim = cl.Large, IsMaternity = cl.Maternity,
        NetworkType = cl.NonNetwork ? "NonNetwork" : "Network", DiseaseCategory = cl.Disease,
    }).ToList();

    // ─────────────────────────── Intake ───────────────────────────

    private sealed class DemoIntakeHandler : IStageHandler
    {
        public string Stage => "Intake";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            // Missing docs = checklist state (NeedsReview detail), NOT an exception —
            // exceptions are for pipeline failures. Unreadable docs DO raise EXC-DOC-UNREADABLE.
            var required = Enums.RequiredDocuments[c.BusinessType];
            var missing = required.Where(rt => !c.Documents.Any(d => d.DocType.Equals(rt, StringComparison.OrdinalIgnoreCase))).ToList();
            var unreadable = c.Documents.Where(d => d.ParseStatus.Equals("Unreadable", StringComparison.OrdinalIgnoreCase)).ToList();

            var detail = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["required"] = required,
                ["missing"] = missing,
                ["unreadable"] = unreadable.Select(d => d.FileName).ToList(),
                ["docCount"] = c.Documents.Count,
            });

            if (unreadable.Count > 0)
                return Task.FromResult(new StageOutcome
                {
                    State = Enums.StageNeedsReview,
                    DetailJson = detail,
                    ToRaise = unreadable.Select(d => new CaseException
                    {
                        CaseId = c.CaseId,
                        Code = Enums.ExcDocUnreadable,
                        Blocking = true,
                        SubjectType = "Document",
                        SubjectRef = d.DocumentId.ToString(),
                        Message = $"Document '{d.FileName}' cannot be parsed (no recognisable header row).",
                        ResolutionTarget = "Case Workbench → document panel",
                        ResumeStage = "Profile",
                    }).ToList(),
                });

            return Task.FromResult(new StageOutcome
            {
                State = missing.Count == 0 ? Enums.StageCompleted : Enums.StageNeedsReview,
                DetailJson = detail,
            });
        }
    }

    // ─────────────────────────── Profile ───────────────────────────

    private sealed class DemoProfileHandler : IStageHandler
    {
        public string Stage => "Profile";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            var columns = 0;
            foreach (var doc in c.Documents.Where(d => d.ParseStatus == "Parsed"))
            {
                var isClaims = doc.DocType == "ClaimsRegister";
                var headers = isClaims ? DemoClaimsHeaders : DemoEnrollmentHeaders;

                var rows = new List<string[]> { headers };
                if (isClaims)
                    foreach (var cl in data.Claims)
                        rows.Add(new[] { cl.ClaimNumber, cl.MemberNumber, cl.Status, cl.Incurred.ToString("0.##"), cl.Large ? "1" : "0", cl.Disease });
                else
                    foreach (var m in data.Members)
                        rows.Add(new[] { m.MemberNumber, m.Relationship, m.Gender, m.Age.ToString(), m.Chronic ? "1" : "0" });

                await repo.AddRawRowsAsync(doc.DocumentId, "Sheet1", rows);

                for (var i = 0; i < headers.Length; i++)
                {
                    var header = headers[i];
                    var isSanctioned = header == "Sanctioned Amt";
                    await repo.AddOrUpdateSourceColumnAsync(new SourceColumn
                    {
                        DocumentId = doc.DocumentId,
                        ColumnIndex = i,
                        Header = header,
                        NonEmptyCount = rows.Count - 1,
                        DistinctCount = isSanctioned ? rows.Count - 1 : Math.Max(2, (rows.Count - 1) / 10),
                        DetectedType = header.Contains("Age") ? "Number" : "Text",
                        SampleValues = rows.Skip(1).Take(3).Select(r => r[Math.Min(i, r.Length - 1)]).ToArray(),
                        TargetParameter = MapHeaderToParameter(header),
                        Confidence = isSanctioned ? 1.00m : 0.99m,
                        ProvenanceTier = isSanctioned ? Enums.TierLearnedAlias : Enums.TierExactDictionary,
                        Reason = isSanctioned ? "Learned alias from a previous case for this TPA" : "Exact dictionary match",
                        IsRequired = IsRequiredParameter(MapHeaderToParameter(header)),
                    });
                    columns++;
                }
            }
            return new StageOutcome
            {
                State = Enums.StageCompleted,
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["columnsProfiled"] = columns }),
            };
        }
    }

    // ─────────────────────── ExtractDocuments ───────────────────────

    private sealed class DemoExtractDocumentsHandler : IStageHandler
    {
        public string Stage => "ExtractDocuments";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            return Task.FromResult(new StageOutcome
            {
                State = Enums.StageCompleted,
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["scheduleLines"] = data.Expiring.Count + data.Proposed.Count,
                    ["avgConfidence"] = 0.96,
                }),
            });
        }
    }

    // ─────────────────────────── Map ───────────────────────────

    private sealed class DemoMapHandler : IStageHandler
    {
        public string Stage => "Map";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var total = 0;
            decimal confSum = 0;
            var lowConf = new List<string>();
            foreach (var doc in c.Documents.Where(d => d.ParseStatus == "Parsed"))
            {
                foreach (var col in await repo.GetSourceColumnsAsync(doc.DocumentId))
                {
                    var suggestion = EngineBridge.MapColumn(col.Header);
                    var target = suggestion?.TargetParameter ?? col.TargetParameter ?? "Unmapped";
                    var conf = suggestion?.Confidence ?? col.Confidence;
                    if (target != "Unmapped")
                        await repo.AddColumnMappingAsync(new ColumnMapping
                        {
                            SourceColumnId = col.SourceColumnId,
                            TargetParameter = target,
                            Confidence = conf,
                            ProvenanceTier = suggestion?.ProvenanceTier ?? col.ProvenanceTier ?? Enums.TierExactDictionary,
                            AutoApproved = conf >= 0.95m,
                            ApprovedBy = conf >= 0.95m ? null : "Auto",
                        });
                    if (col.ProvenanceTier == Enums.TierLearnedAlias)
                        await repo.LearnSynonymAsync(new ColumnSynonym
                        {
                            SynonymText = col.Header,
                            TargetParameter = target,
                            LearnedFromApproval = true,
                            LearnedFromCase = c.CaseNumber,
                        });
                    total++;
                    confSum += conf * 100m;
                    if (conf < 0.80m) lowConf.Add(col.Header);
                }
            }
            var avg = total > 0 ? Math.Round(confSum / total, 2) : 100m;
            var outcome = new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["mappingConfidence"] = avg, ["columnsMapped"] = total }),
            };
            if (lowConf.Count > 0)
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson, ToRaise = new List<CaseException>
                {
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcMapLowConf, Blocking = true,
                        SubjectType = "Column", SubjectRef = string.Join(",", lowConf),
                        Message = $"{lowConf.Count} column(s) below the 0.80 mapping floor.",
                        ResolutionTarget = "Mapping Workbench", ResumeStage = "Map",
                    },
                } };
            return outcome;
        }
    }

    // ─────────────────────── Standardize ───────────────────────

    private sealed class DemoStandardizeHandler : IStageHandler
    {
        public string Stage => "Standardize";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            return Task.FromResult(new StageOutcome
            {
                State = Enums.StageCompleted,
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["dqScore"] = data.DataQualityScore,
                    ["members"] = data.Members.Count,
                    ["claims"] = data.Claims.Count,
                }),
            });
        }
    }

    // ─────────────────────── CodeClinical ───────────────────────

    private sealed class DemoCodeClinicalHandler : IStageHandler
    {
        public string Stage => "CodeClinical";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            var diseases = await repo.GetDiseasesAsync();
            decimal reviewCost = 0;
            var totalCost = data.Claims.Sum(cl => cl.Incurred);
            var unmapped = 0;

            foreach (var claim in data.Claims)
            {
                var match = diseases.FirstOrDefault(d => d.DiseaseName.Equals(claim.Disease, StringComparison.OrdinalIgnoreCase)
                    || d.MatchKeywords.Any(k => claim.Disease.Contains(k, StringComparison.OrdinalIgnoreCase)));
                if (match is null) { unmapped++; continue; }
                // deterministic tier 0.98; ~0.3% of rows land in the review band at 0.85
                // (0.85 sits in the 0.75–0.94 review band per tech §3.9).
                // Selector must NOT assume claim-number shape (real TPAs vary wildly):
                // canonical CL<digits> keeps its §13 semantics; anything else uses a stable hash.
                var n = claim.ClaimNumber.Length > 2 && claim.ClaimNumber[0] == 'C' && claim.ClaimNumber[1] == 'L' && int.TryParse(claim.ClaimNumber.AsSpan(2), out var clN)
                    ? clN
                    : claim.ClaimNumber.Aggregate(17, (acc, ch) => (acc * 31 + ch) & 0x7FFFFFFF);
                var conf = n % 597 == 0 ? 0.85m : 0.98m;
                if (conf is >= 0.75m and < 0.95m) reviewCost += claim.Incurred;
            }

            var reviewSharePct = totalCost > 0 ? Math.Round(reviewCost / totalCost * 100m, 4) : 0m;
            var outcome = new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["coded"] = data.Claims.Count, ["unmapped"] = unmapped, ["reviewSharePct"] = reviewSharePct,
                }),
            };

            if (unmapped > 0)
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson,  ToRaise = new List<CaseException>
                {
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcClinUnmapped, Blocking = true,
                        SubjectType = "ClaimRows", SubjectRef = $"{unmapped}",
                        Message = $"{unmapped} claim row(s) could not be coded at all.",
                        ResolutionTarget = "Clinical Review Queue", ResumeStage = "CodeClinical",
                    },
                } };
            else if (reviewSharePct >= 1m) // blocking when the review band covers ≥ 1% of cost
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson, ToRaise = new List<CaseException>
                {
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcClinReview, Blocking = true,
                        SubjectType = "ClaimRows", SubjectRef = "",
                        Message = $"Coding review band covers {reviewSharePct}% of claim cost (≥ 1%).",
                        ResolutionTarget = "Clinical Review Queue", ResumeStage = "CodeClinical",
                    },
                } };
            else if (reviewSharePct > 0m) // advisory below 1% materiality — pipeline continues
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson,  ToRaise = new List<CaseException>
                {
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcClinReview, Blocking = false,
                        SubjectType = "ClaimRows", SubjectRef = "",
                        Message = $"Coding review band covers {reviewSharePct}% of claim cost (below 1% materiality) — advisory.",
                        ResolutionTarget = "Clinical Review Queue", ResumeStage = "CodeClinical",
                    },
                } };
            return outcome;
        }
    }

    // ─────────────────────────── Analyze ───────────────────────────

    private sealed class DemoAnalyzeHandler : IStageHandler
    {
        public string Stage => "Analyze";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            var snapshot = EngineBridge.ComputeAnalytics(StdMembers(data), StdClaims(data));
            return Task.FromResult(new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["snapshot"] = snapshot,
                    ["dqScore"] = data.DataQualityScore,
                    ["mappingConfidence"] = data.MappingConfidence,
                }),
            });
        }
    }

    // ─────────────────────────── Guardrail ───────────────────────────

    private sealed class DemoGuardrailHandler : IStageHandler
    {
        public string Stage => "Guardrail";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            var snapshot = EngineBridge.ComputeAnalytics(StdMembers(data), StdClaims(data));
            var rules = await repo.GetGuardrailRulesAsync();
            if (rules.Count == 0)
                rules = EngineBridge.StandardSetPlaceholder();

            // Test hook: force the large-claim rule to fail (Refer) for the referral test.
            if (DemoPipelineData.ForceGuardrailFail)
                rules.Add(new GuardrailRuleDef
                {
                    RuleCode = "GRD-LARGE-CLAIM", Category = "Volatility",
                    MetricSelector = "LargeClaimSharePct", Operator = "<=", Threshold = 10m,
                    Severity = "Refer", MessageTemplate = "Large claims {observed}% > 10% (forced test rule).",
                    AppliesToExperienceBusiness = true,
                });

            var result = EngineBridge.EvaluateGuardrails(snapshot, rules, data.DataQualityScore, data.MappingConfidence, c.BusinessType);
            DemoPipelineData.LastGuardrailResult = result;

            var outcome = new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["guardrails"] = result,
                    ["dqScore"] = data.DataQualityScore,
                    ["mappingConfidence"] = data.MappingConfidence,
                    ["outcome"] = result.Outcome,
                    ["confidence"] = result.Confidence,
                }),
            };

            if (result.RulesFailed > 0)
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson, ToRaise = new List<CaseException>
                {
                    // refer ≠ park: the quote still builds and publishes as ReferredToUW
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcGrdFailed, Blocking = false,
                        SubjectType = "Rule",
                        SubjectRef = string.Join(",", result.Evaluations.Where(e => !e.Passed).Select(e => e.RuleCode)),
                        Message = $"{result.RulesFailed} guardrail rule(s) returned refer.",
                        ResolutionTarget = "Guardrail Failures queue", ResumeStage = "Guardrail",
                    },
                } };
            return outcome;
        }
    }

    // ─────────────────────────── Price ───────────────────────────

    private sealed class DemoPriceHandler : IStageHandler
    {
        public string Stage => "Price";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var data = DemoPipelineData.For(c.CaseId);
            var master = repo.GetActiveMasterVersionAsync().GetAwaiter().GetResult();
            var snapshot = EngineBridge.ComputeAnalytics(StdMembers(data), StdClaims(data));
            var delta = EngineBridge.ComputeBenefitDelta(snapshot, data.Expiring, data.Proposed, master, data.Members.Count);
            var pricing = EngineBridge.ComputePricing(snapshot, master, delta, new PricingOverrides());

            DemoPipelineData.LastPricing = pricing;
            DemoPipelineData.LastBenefitDelta = delta.Lines.ToList();

            var outcome = new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["pricing"] = pricing,
                    ["masterVersion"] = master.VersionRef,
                }),
            };

            if (pricing.TechnicalPremium <= 0 || pricing.GrossPremium <= 0)
                outcome = new StageOutcome { State = outcome.State, DetailJson = outcome.DetailJson,  ToRaise = new List<CaseException>
                {
                    new()
                    {
                        CaseId = c.CaseId, Code = Enums.ExcPrcInvalid, Blocking = true,
                        SubjectType = "Master", SubjectRef = master.VersionRef,
                        Message = "Pricing produced a non-positive premium.",
                        ResolutionTarget = "Pricing masters admin", ResumeStage = "Price",
                    },
                } };
            return Task.FromResult(outcome);
        }
    }

    // ─────────────────────── CostBenefits ───────────────────────

    private sealed class DemoCostBenefitsHandler : IStageHandler
    {
        public string Stage => "CostBenefits";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var delta = DemoPipelineData.LastBenefitDelta ?? new List<BenefitDeltaLine>();
            return Task.FromResult(new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["benefitDelta"] = delta,
                    ["totalDeltaPerLife"] = delta.Sum(d => d.DeltaPerLife),
                }),
            });
        }
    }

    // ─────────────────── CompareCompetition ───────────────────

    private sealed class DemoCompareCompetitionHandler : IStageHandler
    {
        public string Stage => "CompareCompetition";

        public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var hasCompetitor = c.Documents.Any(d => d.DocType.Equals("CompetitorQuote", StringComparison.OrdinalIgnoreCase));
            if (!hasCompetitor)
                return Task.FromResult(new StageOutcome
                {
                    State = Enums.StageSkipped,
                    DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["reason"] = "no CompetitorQuote document present" }),
                });

            return Task.FromResult(new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["competitor"] = "Demo Insurer", ["premiumInclGst"] = 17100000m,
                }),
            });
        }
    }

    // ─────────────────────── AssembleQuote ───────────────────────

    private sealed class DemoAssembleQuoteHandler : IStageHandler
    {
        public string Stage => "AssembleQuote";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var pricing = DemoPipelineData.LastPricing
                ?? throw new InvalidOperationException("No pricing result — Price stage must run first.");
            var guardrails = DemoPipelineData.LastGuardrailResult ?? new GuardrailResult();
            var delta = DemoPipelineData.LastBenefitDelta ?? new List<BenefitDeltaLine>();
            var data = DemoPipelineData.For(c.CaseId);
            var snapshot = EngineBridge.ComputeAnalytics(StdMembers(data), StdClaims(data));

            var quote = new QuoteVersion
            {
                CaseId = c.CaseId,
                VersionNo = (await repo.GetQuoteVersionsAsync(c.CaseId)).Count + 1,
                Status = Enums.QuoteCurrent,
                GenerationMode = "Automatic",
                GrossPremium = pricing.TechnicalPremium,
                NetPremium = pricing.TechnicalPremium,
                InputsHash = PipelineOrchestrator.ComputeInputsHash(c),
                Pricing = pricing,
                Analytics = snapshot,
                Guardrails = guardrails,
                BenefitDelta = delta,
            };
            // exactly-one-Current enforced inside AddQuoteVersionAsync (supersede +
            // filtered unique index on SQL; supersede-in-lock on memory)
            await repo.AddQuoteVersionAsync(quote);

            // ── artifact generation (workflow §13: "PDF, Excel pack and JSON generated") ──
            try
            {
                var (json, csv, html) = QuoteArtifacts.Generate(c, quote);
                var baseName = $"{c.CaseNumber}-V{quote.VersionNo}";
                await repo.AddQuoteArtifactAsync(quote.QuoteVersionId, "json", baseName + ".quote.json", Encoding.UTF8.GetBytes(json), "Pipeline");
                await repo.AddQuoteArtifactAsync(quote.QuoteVersionId, "csv", baseName + ".bridge.csv", Encoding.UTF8.GetBytes(csv), "Pipeline");
                await repo.AddQuoteArtifactAsync(quote.QuoteVersionId, "html", baseName + ".quote.html", Encoding.UTF8.GetBytes(html), "Pipeline");
            }
            catch (Exception ex)
            {
                // artifacts are auxiliary — never fail the quote
                Console.Error.WriteLine($"[AssembleQuote] artifact generation skipped: {ex.Message}");
            }

            return new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["quoteVersionId"] = quote.QuoteVersionId,
                    ["versionNo"] = quote.VersionNo,
                    ["grossPremium"] = quote.GrossPremium,
                    ["dqScore"] = data.DataQualityScore,
                    ["mappingConfidence"] = data.MappingConfidence,
                }),
            };
        }
    }

    // ─────────────────────────── Publish ───────────────────────────

    private sealed class DemoPublishHandler : IStageHandler
    {
        public string Stage => "Publish";

        public async Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo)
        {
            var quote = await repo.GetCurrentQuoteVersionAsync(c.CaseId)
                ?? throw new InvalidOperationException("No Current quote — AssembleQuote must run first.");
            var status = quote.Guardrails?.Outcome == "Referred" ? Enums.CaseReferredToUW : Enums.CaseAutoQuoted;

            // notification record via ops.Notification through the repo AddNoteAsync fallback
            await repo.AddNoteAsync(c.CaseId, "Pipeline",
                $"Quote V{quote.VersionNo} published — status {status}, confidence {quote.Guardrails?.Confidence ?? 0}.");

            // ── §12 notification fan-out (in-app) ──
            if (status == Enums.CaseAutoQuoted)
                await Notifier.CaseAutoQuoted(repo, c, quote);
            else
                await Notifier.CaseReferred(repo, c, quote);

            return new StageOutcome
            {
                DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["caseStatus"] = status,
                    ["quoteVersionId"] = quote.QuoteVersionId,
                    ["publishedAtUtc"] = DateTime.UtcNow,
                }),
            };
        }
    }
}

/// <summary>Pass-through handler for services not yet wired.</summary>
public sealed class StubHandler : IStageHandler
{
    public StubHandler(string stage) => Stage = stage;
    public string Stage { get; }

    public Task<StageOutcome> ExecuteAsync(Case c, IGmcRepository repo) =>
        Task.FromResult(new StageOutcome
        {
            State = Enums.StageSkipped,
            DetailJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["stub"] = true }),
        });
}

/// <summary>
/// Per-process demo state + test hooks. ForceGuardrailFail is the injection
/// point the smoke tests use to force a ReferredToUW outcome.
/// </summary>
public static class DemoPipelineData
{
    private static readonly Lazy<StageHandlers.DemoData> SharedData = new(StageHandlers.BuildDemoData);
    /// <summary>The ABC Tech demo dataset, built once per process (deterministic, Random(42)).</summary>
    public static StageHandlers.DemoData Shared => SharedData.Value;
    public static bool ForceGuardrailFail { get; set; }
    public static GuardrailResult? LastGuardrailResult { get; set; }
    public static PricingResult? LastPricing { get; set; }
    public static List<BenefitDeltaLine>? LastBenefitDelta { get; set; }

    // ── per-case real-data overrides (RealDataBridge registers parsed uploads here) ──
    private static readonly object Gate = new();
    private static readonly Dictionary<long, StageHandlers.DemoData> CaseOverrides = new();

    /// <summary>Case data = real uploaded rows when registered, else the demo dataset.</summary>
    public static StageHandlers.DemoData For(long caseId)
    {
        lock (Gate) { return CaseOverrides.TryGetValue(caseId, out var d) ? d : Shared; }
    }

    public static void SetCaseData(long caseId, StageHandlers.DemoData data)
    {
        lock (Gate) { CaseOverrides[caseId] = data; }
    }

    public static bool HasOverride(long caseId)
    {
        lock (Gate) { return CaseOverrides.ContainsKey(caseId); }
    }

    public static void Reset()
    {
        ForceGuardrailFail = false;
        LastGuardrailResult = null;
        LastPricing = null;
        LastBenefitDelta = null;
    }
}
