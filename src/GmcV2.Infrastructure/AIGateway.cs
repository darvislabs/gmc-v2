using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// AI gateway abstraction (architecture §5 AD-1/AD-2): four AI agents sit BEHIND this interface.
/// Deterministic tier is always computed first; the gateway enriches or fills gaps. When no
/// provider is wired (GMCV2_AI_PROVIDER unset) everything degrades to deterministic-only and
/// raises EXC-AI-UNAVAILABLE advisory rather than blocking the pipeline.
/// Provider wiring (NVIDIA NIM / OpenAI / Gemini) is additive: implement IAIProvider, nothing else changes.
/// </summary>
public interface IAIProvider
{
    string Name { get; }
    bool IsAvailable { get; }
}

/// <summary>Extraction agent contract: expiring/proposed schedule fields from PDF text (workflow Stage 3).</summary>
public interface IScheduleExtractionAgent : IAIProvider
{
    Task<Dictionary<string, (decimal? Numeric, string? Text, decimal Confidence)>> ExtractScheduleAsync(string fileName, IReadOnlyList<string> pdfTextLines);
}

/// <summary>Mapping agent contract: column-header → target parameter with confidence (workflow Stage 4).</summary>
public interface IColumnMappingAgent : IAIProvider
{
    Task<(string? TargetParameter, decimal Confidence)> SuggestAsync(string header, IReadOnlyList<string> sampleValues, IReadOnlySet<string> allowedTargets);
}

/// <summary>Clinical agent contract: disease text → ICD-10 code + confidence band (workflow Stage 6).</summary>
public interface IClinicalCodingAgent : IAIProvider
{
    Task<(string? Icd10Code, string? ProcedureCode, decimal Confidence, string? Rationale)> CodeAsync(string diseaseText, IReadOnlyList<(string Icd10, string Description)> diseaseMaster);
}

/// <summary>Narrative agent contract: the quote-story paragraph for Sales (workflow §11).</summary>
public interface INarrativeAgent : IAIProvider
{
    Task<string> NarrateAsync(QuoteVersion quote, Case c, AnalyticsSnapshot snap, GuardrailResult guardrails);
}

/// <summary>Default wiring: all four unavailable → deterministic tier only. Swap when Dhrumil supplies the AI doc + key.</summary>
public sealed class NoopAIProvider : IScheduleExtractionAgent, IColumnMappingAgent, IClinicalCodingAgent, INarrativeAgent
{
    public string Name => "none";
    public bool IsAvailable => false;
    public Task<Dictionary<string, (decimal?, string?, decimal)>> ExtractScheduleAsync(string fileName, IReadOnlyList<string> pdfTextLines) => Task.FromResult(new Dictionary<string, (decimal?, string?, decimal)>());
    public Task<(string?, decimal)> SuggestAsync(string header, IReadOnlyList<string> sampleValues, IReadOnlySet<string> allowedTargets) => Task.FromResult(((string?)null, 0m));
    public Task<(string? Icd10Code, string? ProcedureCode, decimal Confidence, string? Rationale)> CodeAsync(string diseaseText, IReadOnlyList<(string Icd10, string Description)> master) => Task.FromResult(((string?)null, (string?)null, 0m, (string?)null));
    public Task<string> NarrateAsync(QuoteVersion quote, Case c, AnalyticsSnapshot snap, GuardrailResult guardrails) => Task.FromResult("");
}

/// <summary>Runtime registry of wired AI providers (set from env GMCV2_AI_PROVIDER when a real one lands).</summary>
public interface IAIProviderRegistry
{
    IScheduleExtractionAgent? Extraction { get; }
    IColumnMappingAgent? Mapping { get; }
    IClinicalCodingAgent? Clinical { get; }
    INarrativeAgent? Narrative { get; }
    bool AnyAvailable { get; }
}

public sealed class AIProviderRegistry : IAIProviderRegistry
{
    public IScheduleExtractionAgent? Extraction { get; init; }
    public IColumnMappingAgent? Mapping { get; init; }
    public IClinicalCodingAgent? Clinical { get; init; }
    public INarrativeAgent? Narrative { get; init; }
    public bool AnyAvailable => (Extraction?.IsAvailable ?? false) || (Mapping?.IsAvailable ?? false) || (Clinical?.IsAvailable ?? false) || (Narrative?.IsAvailable ?? false);
}
