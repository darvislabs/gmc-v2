# GMC v2 Build Brief — read this first

You are one of several agents building GMC v2 in parallel on this VPS. Other agents are
building other projects in the same solution **right now**. Discipline rules:

## Hard rules
1. Touch ONLY the files listed in your task. Never edit shared contracts:
   `src/GmcV2.Core/**` (Enums.cs, Case.cs, Underwriting.cs, Pricing.cs, Ingestion.cs,
   IGmcRepository.cs). If you need a Core change, list it in your final summary under
   `CORE-CHANGES-NEEDED:` and code against what exists.
2. Never edit other agents' files (listed below so you can avoid them):
   - CALC: src/GmcV2.Calculation/AnalyticsEngine.cs, PricingEngine.cs, BenefitDeltaEngine.cs, GuardrailEngine.cs, StandardizationEngine.cs; tests/GmcV2.Tests/CalculationTests.cs
   - KNOW: src/GmcV2.Knowledge/MappingService.cs, CodingService.cs, MasterResolver.cs, GuardrailRules.cs; tests/GmcV2.Tests/KnowledgeTests.cs
   - INFRW: src/GmcV2.Infrastructure/** , src/GmcV2.Worker/**, sql/DDL/**, tests/GmcV2.Tests/WorkerSmoke.cs
   - WEB: src/GmcV2.Web/**, tests/GmcV2.Tests/WebSmoke.cs
3. Build ONLY your project: `export PATH="$HOME/.dotnet:$PATH"; dotnet build src/GmcV2.Yours -v q`
   → must end `0 Error(s)`. Do NOT run `dotnet test` (other agents' test files may not exist yet).
   Do NOT run `dotnet build` at solution root.
4. .NET 8, C# 12, nullable enable. No external NuGet packages (stdlib + BCL only) except
   Microsoft.Data.SqlClient which INFRW may add to its own csproj.
5. Design-doc sources (read the sections you need, they are long):
   - /home/ubuntu/gmc-v2-docs/gmc_v2_workflow.md — business sequence, stages, exceptions, §7.2 overrides, §7.3 decisions, §8.1/8.2 scenarios, §13 worked example
   - /home/ubuntu/gmc-v2-docs/gmc_v2_architecture.md — layers, AI/calc separation (AD-1), confidence thresholds §5, pricing architecture §6
   - /home/ubuntu/gmc-v2-docs/gmc_v2_db.md — table/column inventories per schema
   - /home/ubuntu/gmc-v2-docs/gmc_v2_technical.md — enumerations §3, module contracts §4, API surface §5, pages §6
   - /home/ubuntu/gmc-v2-docs/gmc_v2_plan.md — phases/gates context

## The contract you compile against (already in Core, do not change)

- `Enums.cs` — all statuses, 13 stage names in `Enums.Stages`, 13 exception codes,
  decision actions, revision reasons, mapping tiers, pricing source types, business types
  + `Enums.RequiredDocuments` per business type.
- `Case.cs` — Case, CaseDocument, CaseStageRun, CaseException, QuoteVersion, UnderwritingAction.
- `Underwriting.cs` — StdMember, StdClaim, AnalyticsSnapshot (all metric fields),
  PricingComponent, PricingResult, GuardrailEvaluation, GuardrailResult.
- `Pricing.cs` — BenefitScheduleLine, BenefitDeltaLine, BenefitDeltaResult,
  PricingMasterVersion (seed defaults in comments), PricingOverrides (HasAny), GuardrailRuleDef.
- `Ingestion.cs` — RawRow, SourceColumn, ColumnMapping, DataQualityResult, ColumnSynonym,
  DiseaseEntry, GoldenEntry.
- `IGmcRepository.cs` — the persistence boundary; every engine takes it (or plain inputs)
  and stays pure/deterministic.

## The three worked examples (acceptance bar)

1. ABC Tech rollover (workflow §13): 1,240 lives; projected burn **₹11,580/life** after
   benefit delta +₹1,190/life; technical premium **₹1,43,50,000**; GST 18% = ₹25,83,000;
   total ₹1,69,33,000. NOTE: 11,580 × 1,240 = 1,43,59,200 ≈ technical premium 1,43,50,000 —
   the doc's own rounding; PricingEngine must implement the chain so that with the worked
   example's inputs the technical premium is ₹1,43,50,000 (use round-to-nearest-100 on the
   per-life figure before multiplying, or per-life 11,572.58… — implement `Math.Round(x/100)*100`
   on the FINAL technical premium and on per-life displays; the test pins the final numbers).
2. Chronic-heavy load-up (workflow §8.1): override-driven new version, delta ≈ +7.9% vs
   current, triggers SeniorUW escalation band (>5% and ≤15%).
3. Large-claim 41% case (workflow §6.4): guardrails produce 2 refers, confidence 71,
   outcome `Referred`.

## Definition of done for every agent
- Your files compile with 0 errors / 0 warnings.
- xUnit test file(s) written, named exactly as assigned, `[Trait("Phase","…")]` optional.
- Final summary: BUILD status, files written, CORE-CHANGES-NEEDED (if any), open questions.
