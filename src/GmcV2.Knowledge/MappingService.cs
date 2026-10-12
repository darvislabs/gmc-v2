using System.Text.Json;
using GmcV2.Core;

namespace GmcV2.Knowledge;

/// <summary>Stub tier interface — semantic model mapping tier, intentionally NOT implemented in v2.0 (AD-2).</summary>
public interface ILlmMappingTier
{
    Task<(string target, decimal confidence)?> MapAsync(string header, string[] samples);
}

/// <summary>
/// Stage 4 — deterministic column mapping (tech §3.8). Tier chain:
/// ExactDictionary → LearnedAlias (scoped) → Fuzzy (normalized Levenshtein/Jaccard ≥0.82)
/// → optional ILlmMappingTier stub → null (confidence 0 → EXC-MAP-LOW-CONF upstream).
/// </summary>
public sealed class MappingService
{
    public MappingService() { }

    public static readonly string[] RequiredParameters =
        { "ClaimNumber", "MemberNumber", "ClaimStatus", "ClaimedAmount", "AdmissionDate" };

    public static readonly string[] EnrollmentRequiredParameters = { "MemberNumber", "Relationship", "DateOfBirth", "Age" };

    /// <summary>Canonical TPA/insurer header dictionary (~60 real headers → target parameters).</summary>
    public static readonly Dictionary<string, string> ExactDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claim no"] = "ClaimNumber", ["claim number"] = "ClaimNumber", ["claimno"] = "ClaimNumber",
        ["claim id"] = "ClaimNumber", ["intimation no"] = "ClaimNumber", ["claim ref no"] = "ClaimNumber",
        ["member id"] = "MemberNumber", ["member no"] = "MemberNumber", ["member number"] = "MemberNumber",
        ["employee id"] = "MemberNumber", ["employee code"] = "MemberNumber", ["emp no"] = "MemberNumber",
        ["health card no"] = "MemberNumber", ["card number"] = "MemberNumber",
        ["patient name"] = "MemberName", ["member name"] = "MemberName", ["employee name"] = "MemberName", ["insured name"] = "MemberName",
        ["relationship"] = "Relationship", ["relation"] = "Relationship", ["relation with employee"] = "Relationship",
        ["gender"] = "Gender", ["sex"] = "Gender",
        ["age"] = "Age",
        ["dob"] = "DateOfBirth", ["d.o.b."] = "DateOfBirth", ["date of birth"] = "DateOfBirth", ["birth date"] = "DateOfBirth",
        ["date of admission"] = "AdmissionDate", ["admission date"] = "AdmissionDate", ["doa"] = "AdmissionDate", ["d.o.a."] = "AdmissionDate",
        ["discharge date"] = "DischargeDate", ["date of discharge"] = "DischargeDate", ["dod"] = "DischargeDate", ["d.o.d."] = "DischargeDate",
        ["room category"] = "RoomCategory", ["room type"] = "RoomCategory", ["ward"] = "RoomCategory", ["room rent"] = "RoomCategory",
        ["hospital"] = "HospitalName", ["hospital name"] = "HospitalName", ["provider"] = "HospitalName", ["provider name"] = "HospitalName",
        ["network"] = "NetworkType", ["network type"] = "NetworkType", ["cashless"] = "IsCashless",
        ["reimbursement"] = "ClaimBasis", ["claim type"] = "ClaimBasis",
        ["claim status"] = "ClaimStatus", ["status"] = "ClaimStatus", ["paid status"] = "ClaimStatus", ["claim stage"] = "ClaimStatus",
        ["paid amount"] = "PaidAmount", ["paid amt"] = "PaidAmount", ["amount paid"] = "PaidAmount", ["settlement amount"] = "PaidAmount",
        ["sanctioned amount"] = "SanctionedAmount", ["sanctioned amt"] = "SanctionedAmount", ["sanction amount"] = "SanctionedAmount",
        ["approved amount"] = "ApprovedAmount", ["approved amt"] = "ApprovedAmount",
        ["claimed amount"] = "ClaimedAmount", ["claimed amt"] = "ClaimedAmount", ["bill amount"] = "ClaimedAmount", ["billed amount"] = "ClaimedAmount", ["gross amount"] = "ClaimedAmount",
        ["net amount"] = "NetAmount", ["net amt"] = "NetAmount", ["net payable"] = "NetAmount",
        ["outstanding amount"] = "OutstandingAmount", ["os amount"] = "OutstandingAmount", ["balance amount"] = "OutstandingAmount",
        ["deduction"] = "Deductions", ["deductions"] = "Deductions", ["deducted amount"] = "Deductions",
        ["copay"] = "Copay", ["co-pay"] = "Copay", ["co pay"] = "Copay", ["copay amount"] = "Copay",
        ["disease"] = "DiagnosisText", ["diagnosis"] = "DiagnosisText", ["ailment"] = "DiagnosisText", ["treatment"] = "DiagnosisText", ["surgery"] = "DiagnosisText",
        ["icd code"] = "IcdCode", ["icd"] = "IcdCode", ["icd 10"] = "IcdCode", ["icd-10"] = "IcdCode",
        ["maternity claim"] = "IsMaternity", ["delivery type"] = "DeliveryType",
        ["claim date"] = "ClaimDate", ["intimation date"] = "ClaimDate", ["bill date"] = "ClaimDate",
    };

    /// <summary>Fuzzy-match candidates: normalized header → target (the dictionary keys).</summary>
    private static List<(string Key, string Target)> _fuzzyPool = new();

    public static void WarmFuzzyPool()
    {
        if (_fuzzyPool.Count == 0)
            _fuzzyPool = ExactDictionary.Select(kv => (Normalize(kv.Key), kv.Value)).ToList();
    }

    public static string Normalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Normalized Levenshtein similarity 0..1.</summary>
    public static decimal Similarity(string a, string b)
    {
        if (a == b) return 1m;
        if (a.Length == 0 || b.Length == 0) return 0m;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        var dist = prev[b.Length];
        return 1m - (decimal)dist / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// Deterministic tier chain for one header.
    /// scope = "Insurer|TPA" — scoped learned aliases only fire for the same insurer/TPA.
    /// </summary>
    public static (string Target, decimal Confidence, string Tier, string Reason, string[] Alternatives)? Suggest(
        string header, List<ColumnSynonym> synonyms, string? scope, string[] samples)
    {
        WarmFuzzyPool();
        var norm = Normalize(header);

        // Tier 1 — exact dictionary
        if (ExactDictionary.TryGetValue(norm, out var t1))
            return (t1, 1.00m, Enums.TierExactDictionary, "Exact dictionary match", Array.Empty<string>());

        // Tier 2 — learned aliases: global first, then scoped (scoped wins when both)
        ColumnSynonym? scoped = null, global = null;
        foreach (var syn in synonyms.Where(s => Normalize(s.SynonymText) == norm))
        {
            if (!string.IsNullOrEmpty(syn.ScopedInsurerTpa))
            {
                if (scope is not null && syn.ScopedInsurerTpa.Equals(scope, StringComparison.OrdinalIgnoreCase)) scoped = syn;
            }
            else global ??= syn;
        }
        if (scoped is not null)
            return (scoped.TargetParameter, 1.00m, Enums.TierLearnedAlias,
                $"Learned alias for {scoped.ScopedInsurerTpa}", Array.Empty<string>());
        if (global is not null)
            return (global.TargetParameter, 1.00m, Enums.TierLearnedAlias, "Learned alias (global)", Array.Empty<string>());

        // Tier 3 — fuzzy on dictionary keys (threshold 0.82; keep top 3 alternatives)
        var scored = _fuzzyPool
            .Select(kv => (kv.Key, kv.Target, Sim: Similarity(norm, kv.Key)))
            .Where(x => x.Sim >= 0.82m)
            .OrderByDescending(x => x.Sim)
            .Take(3)
            .ToList();
        if (scored.Count > 0)
        {
            var best = scored[0];
            var conf = Math.Clamp(0.80m + (best.Sim - 0.82m) / (1m - 0.82m) * 0.14m, 0.80m, 0.94m);
            return (best.Target, Math.Round(conf, 2), Enums.TierFuzzy,
                $"Fuzzy match on '{best.Key}' ({best.Sim:0.00})",
                scored.Skip(1).Select(x => x.Target).Distinct().ToArray());
        }
        // Tier 4 — LLM semantic tier (stub, not wired in v2.0)
        // intentionally returns null → caller raises EXC-MAP-LOW-CONF
        return null;
    }

    /// <summary>Required-parameter coverage for a claims sheet (tech §3.8).</summary>
    public static (string[] Missing, string[] Covered) RequiredCoverage(
        IEnumerable<(string Target, decimal Confidence, string Tier, string Reason, string[] Alternatives)?> suggestions,
        bool enrollment = false)
    {
        var targets = suggestions.Where(s => s is not null).Select(s => s!.Value.Target).ToHashSet();
        var required = enrollment ? EnrollmentRequiredParameters : RequiredParameters;
        var missing = required.Where(r => r == "DateOfBirth" && targets.Contains("Age") ? false : !targets.Contains(r)).ToArray();
        return (missing, required.Except(missing).ToArray());
    }
}
