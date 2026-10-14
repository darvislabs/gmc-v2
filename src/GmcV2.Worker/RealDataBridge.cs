using GmcV2.Core;
using GmcV2.Knowledge;

namespace GmcV2.Worker;

/// <summary>
/// Converts REAL uploaded documents (ing.RawRow, populated by FileParser) into the
/// pipeline's typed dataset. Registers per-case data on DemoPipelineData so the 13
/// stage handlers compute over the client's actual data, with the demo dataset as
/// fallback only. Benefit schedules (Expiring/Proposed) come from text-type documents
/// (Benefit/Value rows) or PDF text lines.
/// </summary>
public static class RealDataBridge
{
    public static bool TryRegister(long caseId, Case c, IGmcRepository repo)
    {
        var members = new List<(string, string, string, int, bool, bool)>();
        var claims = new List<(string, string, string, decimal, bool, bool, bool, string)>();
        var expiring = new List<BenefitScheduleLine>();
        var proposed = new List<BenefitScheduleLine>();

        foreach (var doc in c.Documents)
        {
            var allRows = repo.GetRawRowsAsync(doc.DocumentId).GetAwaiter().GetResult();
            if (allRows.Count == 0) continue;

            // multi-sheet: each sheet carries its own header row — re-detect per sheet,
            // never blend rows across sheets under a single header.
            foreach (var sheetGroup in allRows.GroupBy(r => r.SheetName ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var rows = sheetGroup.OrderBy(r => r.RowNumber).ToList();
                if (rows.Count == 0) continue;
                switch (doc.DocType)
                {
                    case "Enrollment": FillEnrollment(rows, members); break;
                    case "ClaimsRegister": FillClaims(rows, claims); break;
                    case "ExpiringPolicy": FillSchedule(rows, "Expiring", expiring); break;
                    case "Rfq": FillSchedule(rows, "Proposed", proposed); break;
                }
            }
        }

        if (members.Count == 0 && claims.Count == 0) return false;

        var data = new StageHandlers.DemoData();
        FillMembersTyped(members, data);
        FillClaimsTyped(claims, data);
        if (expiring.Count > 0) ReplaceSchedule(data.Expiring, expiring);
        if (proposed.Count > 0) ReplaceSchedule(data.Proposed, proposed);

        DemoPipelineData.SetCaseData(caseId, data);
        return true;
    }

    // ── header helpers ─────────────────────────────────────────────────────
    private static int Col(string[] headers, params string[] names)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            foreach (var n in names)
                if (h.Equals(n, StringComparison.OrdinalIgnoreCase) || h.Replace(" ", "").Equals(n.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        return -1;
    }

    private static void FillEnrollment(List<RawRow> rows, List<(string, string, string, int, bool, bool)> members)
    {
        var headers = rows[0].Cells;
        var cNo = Col(headers, "Member No", "MemberID", "Member ID", "Member Number");
        var cRel = Col(headers, "Relationship", "Relation");
        var cGen = Col(headers, "Gender", "Sex");
        var cAge = Col(headers, "Age", "Age Years");
        var cDob = Col(headers, "DOB", "Date of Birth", "Birth Date");
        var cChronic = Col(headers, "Chronic Flag", "Chronic", "ChronicFlag");

        foreach (var r in rows.Skip(1))
        {
            var cells = r.Cells;
            var no = At(cells, cNo); if (no.Length == 0) continue;
            var rel = NormalizeRel(At(cells, cRel));
            var gender = NormalizeGender(At(cells, cGen));
            var age = 0;
            if (cAge >= 0 && int.TryParse(At(cells, cAge), out var a)) age = a;
            else if (cDob >= 0 && DateTime.TryParse(At(cells, cDob), out var dob)) age = Math.Max(0, (int)((DateTime.UtcNow - dob).TotalDays / 365.25));
            var chronic = cChronic >= 0 && At(cells, cChronic) is "1" or "Y" or "y" or "Yes" or "YES" or "True" or "true";
            members.Add((no, rel, gender, age, chronic, rel == "Parent"));
        }
    }

    private static void FillClaims(List<RawRow> rows, List<(string, string, string, decimal, bool, bool, bool, string)> claims)
    {
        var headers = rows[0].Cells;
        var cClaim = Col(headers, "Claim No", "ClaimNo", "Claim Number", "Claim ID");
        var cMember = Col(headers, "Member No", "MemberID", "Member ID", "Member Number");
        var cStatus = Col(headers, "Claim Status", "Status");
        var cPaid = Col(headers, "Paid Amount", "Paid Amount", "PaidAmt", "Paid");
        var cSanctioned = Col(headers, "Sanctioned Amt", "Sanctioned Amount", "SanctionedAmt", "Approved Amount", "Approved");
        var cClaimed = Col(headers, "Claimed Amount", "ClaimedAmt", "Claimed");
        var cDisease = Col(headers, "Disease", "Diagnosis", "Illness", "Ailment");

        foreach (var r in rows.Skip(1))
        {
            var cells = r.Cells;
            var claimNo = At(cells, cClaim); if (claimNo.Length == 0) continue;
            var memberNo = At(cells, cMember);
            var status = At(cells, cStatus) is { Length: > 0 } s ? s : "Paid";
            var amount = ParseDec(At(cells, cSanctioned)) ?? ParseDec(At(cells, cPaid)) ?? ParseDec(At(cells, cClaimed)) ?? 0m;
            var disease = At(cells, cDisease);
            claims.Add((claimNo, memberNo, status, amount, amount >= 200_000m, disease.Contains("Maternity", StringComparison.OrdinalIgnoreCase) || disease.Contains("Delivery", StringComparison.OrdinalIgnoreCase), false, disease));
        }
    }

    private static void FillSchedule(List<RawRow> rows, string scheduleType, List<BenefitScheduleLine> target)
    {
        var headers = rows[0].Cells;
        var cBenefit = Col(headers, "Benefit", "Benefit Name", "Cover", "Parameter");
        var cValue = Col(headers, "Value", "Amount", "Limit", "Proposed");
        foreach (var r in rows.Skip(1))
        {
            var cells = r.Cells;
            var name = At(cells, cBenefit); if (name.Length == 0) continue;
            var raw = At(cells, cValue);
            var code = NormalizeBenefit(name);
            if (code is null) continue;
            if (decimal.TryParse(raw.TrimEnd('%'), out var num))
                target.Add(new BenefitScheduleLine { ScheduleType = scheduleType, BenefitCode = code, NumericValue = num, Confidence = 0.95m, Citation = docCitation(scheduleType) });
            else
                target.Add(new BenefitScheduleLine { ScheduleType = scheduleType, BenefitCode = code, TextValue = raw, Confidence = 0.95m, Citation = docCitation(scheduleType) });
        }
    }

    private static string docCitation(string t) => t == "Expiring" ? "Expiring policy (parsed)" : "RFQ (parsed)";

    private static string? NormalizeBenefit(string name)
    {
        var n = name.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        return n switch
        {
            "basesuminsured" or "suminsured" or "si" or "base si" => "BaseSumInsured",
            "maternitynormal" or "normaldelivery" or "maternity(normal)" => "MaternityNormal",
            "maternitycs" or "caesarean" or "cesarean" or "maternity(c-section)" or "csection" => "MaternityCs",
            "ambulance" or "ambulancecover" => "Ambulance",
            "newborn" or "newborncover" => "Newborn",
            "roomrent" or "roomrentlimit" or "roomrentcapping" => "RoomRent",
            "pedday1" or "pedwaiver" or "pedwaitingperiod" => "PedDay1",
            _ => null,
        };
    }

    // ── typed fills (tuple element names must match DemoData) ──────────────
    private static void FillMembersTyped(
        List<(string MemberNumber, string Relationship, string Gender, int Age, bool Chronic, bool Parent)> src,
        StageHandlers.DemoData data)
    {
        foreach (var m in src) data.Members.Add(m);
    }

    private static void FillClaimsTyped(
        List<(string ClaimNumber, string MemberNumber, string Status, decimal Incurred, bool Large, bool Maternity, bool NonNetwork, string Disease)> src,
        StageHandlers.DemoData data)
    {
        foreach (var cl in src) data.Claims.Add(cl);
    }

    private static void ReplaceSchedule(List<BenefitScheduleLine> original, List<BenefitScheduleLine> parsed)
    {
        original.Clear();
        foreach (var l in parsed) original.Add(l);
    }

    // ── cell helpers ───────────────────────────────────────────────────────
    private static string At(string[] cells, int idx) => idx >= 0 && idx < cells.Length ? cells[idx].Trim() : "";
    private static decimal? ParseDec(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace("₹", "").Replace(",", "").Replace("INR", "").Trim();
        return decimal.TryParse(t, out var d) ? d : null;
    }
    private static string NormalizeRel(string s) => s.Trim().ToLowerInvariant() switch
    {
        "employee" or "self" or "member" => "Employee",
        "spouse" or "wife" or "husband" => "Spouse",
        "son" or "daughter" or "child" or "kid" => "Child",
        "father" or "mother" or "parent" or "parents" or "fil" or "mil" => "Parent",
        _ => s.Trim(),
    };
    private static string NormalizeGender(string s) => s.Trim().ToUpperInvariant() switch
    {
        "M" or "MALE" => "Male",
        "F" or "FEMALE" => "Female",
        _ => s.Trim(),
    };
}
