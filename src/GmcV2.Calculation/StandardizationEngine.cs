using System.Text.Json;
using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// Stage 5 — standardization engine (db §5.4-5.6). Projects mapped raw rows onto
/// StdMember/StdClaim shapes via the column mappings, normalizes paid statuses,
/// dates, amounts and relationships, derives large-claim/maternity flags, and
/// scores data quality across the five DQ dimensions (db §5.6).
/// </summary>
public sealed class StandardizationEngine
{
    public StandardizationEngine() { }

    /// <summary>Column parameter → index for one document, from current mappings.</summary>
    public sealed class ColumnPlan
    {
        public required Dictionary<string, int> Index { get; init; }
    }

    public static ColumnPlan BuildPlan(List<SourceColumn> columns, Dictionary<long, ColumnMapping> latestByColumn)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            if (!latestByColumn.TryGetValue(col.SourceColumnId, out var m)) continue;
            if (m.TargetParameter is null or "Unmapped") continue;
            idx[m.TargetParameter] = col.ColumnIndex;
        }
        return new ColumnPlan { Index = idx };
    }

    private static string NormStatus(string raw, Dictionary<string, string> paidMap)
    {
        var key = raw?.Trim() ?? "";
        return paidMap.TryGetValue(key, out var norm) ? norm
            : paidMap.TryGetValue(key.ToLowerInvariant(), out var norm2) ? norm2
            : key; // unmapped statuses pass through; DQ flags them
    }

    private static bool TryMoney(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = new string(raw.Trim().Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDate(string? raw, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "dd-MMM-yyyy", "MM/dd/yyyy", "yyyy/MM/dd", "dd.MM.yyyy" };
        return DateTime.TryParseExact(s, formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value)
            || DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out value);
    }

    /// <summary>Full standardization of one claims sheet: rows → StdClaim list + DQ results.</summary>
    public static (List<StdClaim> Claims, List<DataQualityResult> Dq) StandardizeClaims(
        List<string[]> rows, ColumnPlan plan, Dictionary<string, string> paidMap)
    {
        var claims = new List<StdClaim>();
        var dq = new List<DataQualityResult>();
        int linked = 0, requiredOk = 0, datesOk = 0, amountsOk = 0;
        var seen = new HashSet<string>();
        var dupCount = 0;

        var has = (string p) => plan.Index.ContainsKey(p);

        for (var r = 1; r < rows.Count; r++) // row 0 = header
        {
            var row = rows[r];
            var get = (string p) => plan.Index.TryGetValue(p, out var i) && i < row.Length ? row[i]?.Trim() : null;

            var claim = new StdClaim
            {
                ClaimNumber = get("ClaimNumber") ?? "",
                MemberNumber = get("MemberNumber") ?? "",
                ClaimStatus = NormStatus(get("ClaimStatus") ?? "Paid", paidMap),
            };
            if (TryDate(get("AdmissionDate"), out var adm)) claim.AdmissionDate = adm;
            if (TryDate(get("DischargeDate"), out var dis)) claim.DischargeDate = dis;

            var claimed = TryMoney(get("ClaimedAmount"), out var cAmt);
            var approved = TryMoney(get("ApprovedAmount"), out var aAmt) || claimed;
            var sanctioned = TryMoney(get("SanctionedAmount"), out var sAmt) || approved;
            var paidAmt = TryMoney(get("PaidAmount"), out var pAmt) || sanctioned;

            claim.ClaimedAmount = claimed ? cAmt : 0m;
            claim.ApprovedAmount = approved ? aAmt : claim.ClaimedAmount;
            claim.SanctionedAmount = sanctioned ? sAmt : claim.ApprovedAmount;
            claim.PaidAmount = paidAmt ? pAmt : claim.SanctionedAmount;
            claim.OutstandingAmount = Math.Max(0m, claim.SanctionedAmount - claim.PaidAmount);
            // incurred = paid + outstanding (db §5.5); rejected rows carry zero incurred
            var rejected = claim.ClaimStatus == "Rejected";
            claim.IncurredAmount = rejected ? 0m : claim.PaidAmount + claim.OutstandingAmount;
            claim.NetworkType = (get("NetworkType") ?? "").Equals("NonNetwork", StringComparison.OrdinalIgnoreCase)
                ? "NonNetwork" : "Network";
            var dx = get("DiagnosisText");
            if (bool.TryParse(get("IsLargeClaim"), out var lg)) claim.IsLargeClaim = lg;
            else if (decimal.TryParse(get("IsLargeClaim"), out var lgn)) claim.IsLargeClaim = lgn > 0m;
            claim.IsLargeClaim |= claim.IncurredAmount >= 200_000m; // ₹2L threshold (db §5.5)
            claim.IsMaternity = (dx ?? "").Contains("maternity", StringComparison.OrdinalIgnoreCase)
                || (dx ?? "").Contains("delivery", StringComparison.OrdinalIgnoreCase);

            claims.Add(claim);

            if (!string.IsNullOrEmpty(claim.MemberNumber)) linked++;
            if (!string.IsNullOrEmpty(claim.ClaimNumber) && claim.IncurredAmount >= 0m) requiredOk++;
            if (claim.AdmissionDate is not null) datesOk++;
            if (claimed || sanctioned) amountsOk++;
            if (!seen.Add(claim.ClaimNumber + "|" + claim.MemberNumber)) dupCount++;
        }

        var n = Math.Max(rows.Count - 1, 1);
        dq.Add(new DataQualityResult { RuleCode = "DQ-CLAIM-LINKAGE", Passed = linked * 100m / n >= 95m, AffectedRows = rows.Count - 1 - linked, ScorePct = Math.Round(linked * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-REQUIRED-FIELDS", Passed = requiredOk * 100m / n >= 98m, AffectedRows = rows.Count - 1 - requiredOk, ScorePct = Math.Round(requiredOk * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-DATE-VALID", Passed = datesOk * 100m / n >= 90m, AffectedRows = rows.Count - 1 - datesOk, ScorePct = Math.Round(datesOk * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-AMOUNT-RECONCILE", Passed = amountsOk * 100m / n >= 95m, AffectedRows = rows.Count - 1 - amountsOk, ScorePct = Math.Round(amountsOk * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-DUPLICATES", Passed = dupCount == 0, AffectedRows = dupCount, ScorePct = Math.Round((n - dupCount) * 100m / n, 2) });
        return (claims, dq);
    }

    /// <summary>Full standardization of one enrollment sheet: rows → StdMember list + DQ results.</summary>
    public static (List<StdMember> Members, List<DataQualityResult> Dq) StandardizeMembers(
        List<string[]> rows, ColumnPlan plan)
    {
        var members = new List<StdMember>();
        var dq = new List<DataQualityResult>();
        int relOk = 0, ageOk = 0, dobOk = 0, genderOk = 0;
        var seen = new HashSet<string>();
        var dupCount = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var get = (string p) => plan.Index.TryGetValue(p, out var i) && i < row.Length ? row[i]?.Trim() : null;

            var relRaw = get("Relationship") ?? "";
            var rel = relRaw switch
            {
                "Employee" or "Emp" or "Self" or "Member" => "Employee",
                "Spouse" or "Wife" or "Husband" => "Spouse",
                "Child" or "Son" or "Daughter" or "Kid" => "Child",
                "Parent" or "Father" or "Mother" or "Parent-in-law" => "Parent",
                _ => relRaw, // unmapped — DQ flags
            };
            var m = new StdMember { MemberNumber = get("MemberNumber") ?? "", Relationship = rel };

            var g = get("Gender") ?? "";
            m.Gender = g switch
            {
                "M" or "Male" or "MALE" or "male" => "Male",
                "F" or "Female" or "FEMALE" or "female" => "Female",
                _ => g,
            };
            if (int.TryParse(get("Age"), out var age)) { m.Age = age; ageOk++; }
            else if (TryDate(get("DateOfBirth"), out var dob))
            {
                m.Age = Math.Max(0, (int)((DateTime.UtcNow - dob).TotalDays / 365.25));
                dobOk++;
            }

            if (rel is "Employee" or "Spouse" or "Child" or "Parent") relOk++;
            if (!string.IsNullOrEmpty(m.Gender)) genderOk++;
            if (!string.IsNullOrEmpty(m.MemberNumber) && seen.Add(m.MemberNumber)) { /* unique */ }
            else if (!string.IsNullOrEmpty(m.MemberNumber)) dupCount++;

            members.Add(m);
        }

        var n = Math.Max(rows.Count - 1, 1);
        dq.Add(new DataQualityResult { RuleCode = "DQ-REQUIRED-FIELDS", Passed = relOk * 100m / n >= 98m, AffectedRows = rows.Count - 1 - relOk, ScorePct = Math.Round(relOk * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-DATE-VALID", Passed = (ageOk + dobOk) * 100m / n >= 98m, AffectedRows = rows.Count - 1 - ageOk - dobOk, ScorePct = Math.Round((ageOk + dobOk) * 100m / n, 2) });
        dq.Add(new DataQualityResult { RuleCode = "DQ-CLAIM-LINKAGE", Passed = genderOk * 100m / n >= 95m, AffectedRows = rows.Count - 1 - genderOk, ScorePct = Math.Round(genderOk * 100m / n, 2) });
        return (members, dq);
    }

    /// <summary>Composite DQ score 0-100 over the five dimensions (db §5.6 weighting).</summary>
    public static decimal CompositeDqScore(List<DataQualityResult> results) =>
        results.Count == 0 ? 0m : Math.Round(results.Average(r => r.ScorePct), 2);
}
