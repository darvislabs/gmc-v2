using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Ollama.com OpenAI-compatible provider (free tier, locked design): chat completions over
/// HttpClient + System.Text.Json, no extra packages. Model split is fixed by design:
/// gpt-oss:120b for Extraction + Narrative, gpt-oss:20b for Mapping + Clinical.
/// The API key comes only from the environment (GMCV2_AI_KEY) and is never logged or stored.
/// Every method is best-effort: callers apply the 20s timeout + deterministic fallback (AD-2),
/// so AI failures never block the pipeline; AI fills gaps only and never touches premium math (AD-1).
/// </summary>
public sealed class OllamaAIProvider : IScheduleExtractionAgent, IColumnMappingAgent, IClinicalCodingAgent, INarrativeAgent
{
    private const string ExtractionModel = "gpt-oss:120b";
    private const string FastModel = "gpt-oss:20b";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _baseUrl;
    private readonly string _apiKey;

    public OllamaAIProvider(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public string Name => "ollama";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Env factory: activates only when GMCV2_AI_PROVIDER == "ollama" (else null). The key is
    /// optional at construction (unavailable provider) but required for any call to succeed.
    /// GMCV2_AI_BASEURL overrides the default https://ollama.com/v1.
    /// </summary>
    public static OllamaAIProvider? FromEnv()
    {
        if (!"ollama".Equals(Environment.GetEnvironmentVariable("GMCV2_AI_PROVIDER")?.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        var baseUrl = Environment.GetEnvironmentVariable("GMCV2_AI_BASEURL");
        return new OllamaAIProvider(
            string.IsNullOrWhiteSpace(baseUrl) ? "https://ollama.com/v1" : baseUrl.Trim(),
            Environment.GetEnvironmentVariable("GMCV2_AI_KEY") ?? "");
    }

    public async Task<Dictionary<string, (decimal?, string?, decimal)>> ExtractScheduleAsync(string fileName, IReadOnlyList<string> pdfTextLines)
    {
        var result = new Dictionary<string, (decimal?, string?, decimal)>(StringComparer.OrdinalIgnoreCase);
        var reply = await ChatAsync(ExtractionModel,
            "You extract benefit-schedule fields from group health insurance documents. Reply with one field per line as 'Field = value'. Numbers plain, no currency symbols. No other text.",
            $"File: {fileName}\nText:\n{string.Join("\n", pdfTextLines.Take(150))}");
        if (string.IsNullOrWhiteSpace(reply)) return result;
        foreach (var line in reply.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var field = line[..eq].Trim(' ', '-', '*', '•', '\t');
            var raw = line[(eq + 1)..].Trim();
            if (field.Length == 0 || raw.Length == 0) continue;
            var digits = new string(raw.TakeWhile(ch => char.IsDigit(ch) || ch is '.' or ',').ToArray()).Replace(",", "");
            if (decimal.TryParse(digits, out var num))
                result[field] = (num, raw, 0.85m);
            else
                result[field] = ((decimal?)null, raw, 0.85m);
        }
        return result;
    }

    public async Task<(string?, decimal)> SuggestAsync(string header, IReadOnlyList<string> sampleValues, IReadOnlySet<string> allowedTargets)
    {
        var reply = await ChatAsync(FastModel,
            "You map spreadsheet column headers to canonical parameter names. Reply with EXACTLY one name from the allowed list, or NONE. No other text.",
            $"Allowed targets: {string.Join(", ", allowedTargets)}\nColumn header: {header}\nSample values: {string.Join(" | ", sampleValues.Take(5))}");
        var t = Clean(reply);
        if (t is null || t.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return (null, 0m);
        var hit = allowedTargets.FirstOrDefault(a => a.Equals(t, StringComparison.OrdinalIgnoreCase));
        return hit is null ? (null, 0m) : (hit, 0.85m);
    }

    public async Task<(string? Icd10Code, string? ProcedureCode, decimal Confidence, string? Rationale)> CodeAsync(string diseaseText, IReadOnlyList<(string Icd10, string Description)> diseaseMaster)
    {
        var list = string.Join("\n", diseaseMaster.Take(120).Select(d => $"{d.Icd10} — {d.Description}"));
        var reply = await ChatAsync(FastModel,
            "You are an ICD-10 medical coder for group health insurance claims. Pick the single best ICD-10 code from the provided master list for the diagnosis text. Reply with EXACTLY 'ICD|confidence' where ICD is the code (or NONE) and confidence is 0.00-1.00. No other text.",
            $"Master list:\n{list}\n\nDiagnosis text: {diseaseText}");
        // reply shape "E11.9|0.9" — tolerate prose by scanning for the last code-like token
        foreach (var raw in (reply ?? "").Split('\n').Reverse())
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var code = parts[0].ToUpperInvariant();
            if (!diseaseMaster.Any(d => d.Icd10.Equals(code, StringComparison.OrdinalIgnoreCase))) continue;
            var conf = decimal.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var c) ? Math.Clamp(c, 0m, 1m) : 0.85m;
            return (code, null, conf, $"ollama ({FastModel}): {diseaseText}");
        }
        return (null, null, 0m, null);
    }

    public async Task<string> NarrateAsync(QuoteVersion quote, Case c, AnalyticsSnapshot snap, GuardrailResult guardrails)
    {
        var reply = await ChatAsync(ExtractionModel,
            "You write a short underwriting narrative (one paragraph, professional tone) for a group health insurance quote summary. Use only the figures provided. No headings, no lists.",
            $"Client: {c.ClientName}\nBusiness: {c.BusinessType} {c.BusinessSubType}\nLives: {snap.CoveredLives}; claims: {snap.ClaimCount}; incurred: {snap.AmountIncurred}; burn/life: {snap.BurnCostPerLife}; chronic %: {snap.ChronicPrevalencePct}; large-claim %: {snap.LargeClaimSharePct}.\nGuardrails: {guardrails.Outcome} at confidence {guardrails.Confidence}.\nTechnical premium: {quote.Pricing?.TechnicalPremium ?? quote.GrossPremium}");
        return (reply ?? "").Trim();
    }

    /// <summary>Single chat-completion call; returns the message content or null on any failure (timeout, non-2xx, bad JSON). Never throws, never logs the key.</summary>
    private async Task<string?> ChatAsync(string model, string system, string user)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
                temperature = 0.1,
            }, JsonOpts), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OllamaAI] chat call failed: {ex.GetType().Name}: {ex.Message[..Math.Min(160, ex.Message.Length)]}"); // TEMP diagnostic
            return null; // AD-2: AI failure is advisory only
        }
    }

    private static string? Clean(string? s)
    {
        var t = (s ?? "").Trim().Trim('.', ':', '-', '*', '`', '"', '\'');
        return t.Length == 0 ? null : t;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
