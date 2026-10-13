using GmcV2.Core;

namespace GmcV2.Knowledge;

/// <summary>Stub tier interface — semantic model coding tier, intentionally NOT implemented in v2.0 (AD-2).</summary>
public interface ILlmCodingTier
{
    Task<(string icd1, string? icd2, decimal confidence)?> CodeAsync(string diagnosisText, string[] samples);
}

public sealed record CodingDecision(int RowIdx, string? IcdLevel1, string? IcdLevel2,
    decimal Confidence, string Tier, bool NeedsReview, string? Suggestion);

public sealed class CodingOutcome
{
    public List<CodingDecision> Decisions { get; init; } = new();
    public int MasterHits => Decisions.Count(d => d.Tier == Enums.TierMaster);
    public int GoldenHits => Decisions.Count(d => d.Tier == Enums.TierGolden);
    public int ReviewCount => Decisions.Count(d => d.NeedsReview);
    public List<int> UnmappedRows { get; init; } = new();
}

/// <summary>
/// Stage 6 — deterministic clinical coding (tech §3.9). Tier chain: master taxonomy
/// (exact normalized name → keyword) → golden dataset (normalized equality / ≥0.9
/// Jaccard) → stub ILlmCodingTier (not wired in v2.0) → unmapped review row.
/// Never invents ICD codes — only codes present on DiseaseEntry rows are used.
/// </summary>
public sealed class CodingService
{
    public CodingService() { }

    private readonly Dictionary<string, DiseaseEntry> _byName;
    private readonly List<(string[] Keywords, DiseaseEntry Entry)> _byKeyword;
    private readonly Dictionary<string, (GoldenEntry Entry, decimal Sim)>? _golden;

    public CodingService(List<DiseaseEntry> diseases, List<GoldenEntry> golden)
    {
        _byName = new Dictionary<string, DiseaseEntry>(StringComparer.OrdinalIgnoreCase);
        _byKeyword = new List<(string[], DiseaseEntry)>();
        foreach (var d in diseases)
        {
            _byName[Norm(d.DiseaseName)] = d;
            foreach (var k in d.MatchKeywords) _byKeyword.Add((new[] { k }, d));
        }
        _golden = golden.ToDictionary(g => Norm(g.RawTextNorm), g => (g, 1.00m));
    }

    public static string Norm(string s)
    {
        s = s.ToLowerInvariant();
        // abbreviation expansion (tech §3.9)
        var expansions = new (string Pat, string Rep)[]
        {
            (@"\bk/c/o\b", "known case of"), (@"\bc/o\b", "complaint of"),
            (@"\bdm\b", "diabetes mellitus"), (@"\bd\.m\.\b", "diabetes mellitus"),
            (@"\bhtn\b", "hypertension"), (@"\bcad\b", "coronary artery disease"),
            (@"\bckd\b", "chronic kidney disease"), (@"\bcopd\b", "chronic obstructive pulmonary disease"),
            (@"\bptbs\b", "pulmonary tuberculosis"), (@"\bpts\b", "pulmonary tuberculosis"),
            (@"\bca\b(?!\s*n\b)", "carcinoma"), // 'ca breast' → carcinoma; 'cabg' untouched
        };
        foreach (var (pat, rep) in expansions)
            s = System.Text.RegularExpressions.Regex.Replace(s, pat, rep);
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray();
        return new string(chars).Trim();
    }

    private static decimal Jaccard(string a, string b)
    {
        var A = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var B = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (A.Count == 0 || B.Count == 0) return 0m;
        return (decimal)A.Count(x => B.Contains(x)) / A.Union(B).Count();
    }

    /// <summary>Bridge-compatible static entry: rows of (rowIdx, diagnosisText) → CodingOutcome.</summary>
    public static CodingOutcome Code(IEnumerable<(int RowIdx, string Text)> rows,
        List<DiseaseEntry> diseases, List<GoldenEntry> golden)
    {
        var svc = new CodingService(diseases, golden);
        return svc.CodeMany(rows);
    }

    public CodingOutcome CodeMany(IEnumerable<(int RowIdx, string Text)> rows)
    {
        var outcome = new CodingOutcome();
        foreach (var (idx, text) in rows)
            outcome.Decisions.Add(CodeOne(idx, text));
        outcome.UnmappedRows.AddRange(outcome.Decisions.Where(d => d.IcdLevel1 is null).Select(d => d.RowIdx));
        return outcome;
    }

    public CodingDecision CodeOne(int rowIdx, string text)
    {
        var norm = Norm(text ?? "");
        if (norm.Length == 0)
            return new CodingDecision(rowIdx, null, null, 0m, Enums.TierMaster, NeedsReview: true, Suggestion: "empty diagnosis text");

        // Tier 1 — golden dataset (human-confirmed text → ICD) wins when exact
        if (_golden.TryGetValue(norm, out var g))
            return new CodingDecision(rowIdx, g.Entry.IcdLevel1, g.Entry.IcdLevel2, 0.97m, Enums.TierGolden,
                NeedsReview: false, Suggestion: $"golden match ({g.Entry.GoldenVersion})");

        // Tier 2a — master exact normalized disease name
        if (_byName.TryGetValue(norm, out var exact))
            return new CodingDecision(rowIdx, exact.IcdLevel1, exact.IcdLevel2, 1.00m, Enums.TierMaster,
                NeedsReview: false, Suggestion: $"master: {exact.DiseaseName}");

        // Tier 2b — golden high-similarity (≥0.9 Jaccard)
        foreach (var (key, (entry, _)) in _golden)
        {
            var sim = Jaccard(norm, key);
            if (sim >= 0.9m)
                return new CodingDecision(rowIdx, entry.IcdLevel1, entry.IcdLevel2, 0.97m, Enums.TierGolden,
                    NeedsReview: false, Suggestion: $"golden Jaccard {sim:0.00}");
        }

        // Tier 2c — master keyword containment (0.90, review band)
        foreach (var (keywords, entry) in _byKeyword)
        {
            var hit = keywords.FirstOrDefault(k => norm.Contains(Norm(k), StringComparison.Ordinal));
            if (hit is not null)
                return new CodingDecision(rowIdx, entry.IcdLevel1, entry.IcdLevel2, 0.90m, Enums.TierMaster,
                    NeedsReview: true, Suggestion: $"keyword '{hit}' → {entry.DiseaseName} (review band)");
        }

        // Tier 3 — LLM tier stub (not wired in v2.0) → unmapped review row
        return new CodingDecision(rowIdx, null, null, 0m, Enums.TierFuzzy, NeedsReview: true, Suggestion: "no deterministic match — clinical review");
    }

    /// <summary>Chronic flag helper — a decision is chronic when its master entry is chronic.</summary>
    public bool IsChronicHit(CodingDecision d) =>
        d.IcdLevel1 is not null && d.Tier == Enums.TierMaster &&
        _byName.Values.Any(e => e.IcdLevel1 == d.IcdLevel1 && e.IsChronic);
}
