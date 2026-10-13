using System.Text;
using System.Text.Json;
using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Quote artifact generation (workflow §13): quote JSON, bridge CSV, printable HTML quote pack.
/// Deterministic — every number comes from the persisted PricingResult. Stored in uw.QuoteArtifact.
/// </summary>
public static class QuoteArtifacts
{
    public static (string Json, string Csv, string Html) Generate(Case c, QuoteVersion q)
    {
        var p = q.Pricing ?? new PricingResult();
        var comps = p.Components.OrderBy(x => x.StepOrder).ToList();
        var g = q.Guardrails ?? new GuardrailResult();

        var json = JsonSerializer.Serialize(new
        {
            quote = new
            {
                q.QuoteVersionId, q.CaseId, q.VersionNo, q.Status, q.GenerationMode,
                q.GrossPremium, q.NetPremium, q.CreatedAtUtc, q.CreatedBy,
            },
            case_ = new { c.CaseId, c.CaseNumber, c.ClientName, c.BusinessType, c.BusinessSubType, c.Status },
            pricing = p,
            guardrails = g,
            analytics = q.Analytics,
            benefitDelta = q.BenefitDelta,
            generatedAtUtc = DateTime.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });

        var csv = new StringBuilder();
        csv.AppendLine("Step,Component,Value,Unit,Basis,Override,Override Reason");
        foreach (var comp in comps)
        {
            csv.AppendLine(string.Join(",",
                Csv(comp.StepOrder.ToString()), Csv(comp.ComponentName), Csv(comp.Value.ToString("0.00##")),
                Csv(comp.Unit), Csv(comp.Basis ?? ""), comp.IsOverride ? "yes" : "no", Csv(comp.OverrideReason ?? "")));
        }
        csv.AppendLine();
        csv.AppendLine("Totals");
        csv.AppendLine($"Technical Premium,{p.TechnicalPremium:0.00}");
        csv.AppendLine($"Total Premium (incl GST),{comps.LastOrDefault(c2 => c2.ComponentName.Contains("Total", StringComparison.OrdinalIgnoreCase))?.Value ?? 0:0.00}");

        var gstComp = comps.LastOrDefault(x => x.ComponentName.StartsWith("GST", StringComparison.OrdinalIgnoreCase));
        var totalComp = comps.LastOrDefault(x => x.ComponentName.Contains("Total premium", StringComparison.OrdinalIgnoreCase));
        var html = BuildHtml(c, q, p, g, gstComp?.Value ?? 0, totalComp?.Value ?? 0);
        return (json, csv.ToString(), html);
    }

    private static string BuildHtml(Case c, QuoteVersion q, PricingResult p, GuardrailResult g, decimal gst, decimal total)
    {
        var sb = new StringBuilder();
        sb.Append("""<!doctype html><html><head><meta charset="utf-8"><title>""");
        sb.Append(Esc($"Group Mediclaim Quote — {c.ClientName}"));
        sb.Append("</title><style>"
        + "body{font-family:Georgia,serif;color:#1c1c1e;max-width:820px;margin:40px auto;padding:0 24px;line-height:1.5}"
        + ".brand{font-size:11px;letter-spacing:.22em;text-transform:uppercase;color:#8a7a5c;font-family:monospace}"
        + "h1{font-size:26px;margin:6px 0 2px;font-weight:600}"
        + ".sub{color:#666;font-size:14px;margin-bottom:20px}"
        + "hr{border:none;border-top:1px solid #ddd;margin:18px 0}"
        + "table{width:100%;border-collapse:collapse;font-size:13px;margin:10px 0}"
        + "th{text-align:left;border-bottom:2px solid #1c1c1e;padding:6px 8px;font-size:11px;text-transform:uppercase;letter-spacing:.06em}"
        + "td{border-bottom:1px solid #eee;padding:6px 8px}"
        + "td.num{text-align:right;font-variant-numeric:tabular-nums}"
        + ".kpis{display:flex;gap:14px;margin:16px 0}"
        + ".kpi{flex:1;border:1px solid #ddd;border-radius:10px;padding:12px 14px}"
        + ".kpi .l{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:#777;font-family:monospace}"
        + ".kpi .v{font-size:20px;font-weight:600;margin-top:4px}"
        + ".ov{color:#9a6a00;font-size:11px}"
        + ".badge{display:inline-block;border:1px solid #ccc;border-radius:999px;padding:2px 10px;font-size:11px;font-family:monospace}"
        + "footer{margin-top:28px;color:#999;font-size:11px;font-family:monospace}"
        + "</style></head><body>");
        sb.Append($"""<div class="brand">OPEXMINDS · GROUP MEDICLAIM · QUOTE V{q.VersionNo}</div>""");
        sb.Append($"""<h1>{Esc(c.ClientName)}</h1>""");
        sb.Append($"""<div class="sub">{Esc(c.CaseNumber)} · {Esc(c.BusinessType)} · {Esc(c.BusinessSubType)} · master {Esc(p.MasterVersionRef)} · {DateTime.UtcNow:dd MMM yyyy}</div>""");
        sb.Append($"""<span class="badge">{Esc(q.Status)}</span>""");
        sb.Append($"""<div class="kpis"><div class="kpi"><div class="l">Technical premium</div><div class="v">₹{p.TechnicalPremium:N0}</div></div><div class="kpi"><div class="l">GST @ 18%</div><div class="v">₹{gst:N0}</div></div><div class="kpi"><div class="l">Total payable</div><div class="v">₹{total:N0}</div></div></div>""");
        sb.Append("""<hr><div class="brand">PRICING BRIDGE</div><table><tr><th>#</th><th>Component</th><th style="text-align:right">Value</th><th>Basis</th></tr>""");
        foreach (var comp in p.Components.OrderBy(x => x.StepOrder))
        {
            var ov = comp.IsOverride ? """ <span class="ov">override</span>""" : "";
            sb.Append($"""<tr><td>{comp.StepOrder}</td><td>{Esc(comp.ComponentName)}{ov}</td><td class="num">{comp.Value:N2} {Esc(comp.Unit)}</td><td>{Esc(comp.Basis ?? "")}</td></tr>""");
        }
        sb.Append("</table>");
        sb.Append("""<hr><div class="brand">GUARDRAILS</div><table><tr><th>Rule</th><th>Observed</th><th>Threshold</th><th>Result</th></tr>""");
        foreach (var e in g.Evaluations)
            sb.Append($"""<tr><td>{Esc(e.RuleCode)}</td><td class="num">{e.Observed}</td><td class="num">{e.Operator} {e.Threshold}</td><td>{(e.Passed ? "✓ pass" : "✗ fail")} {Esc(e.Severity)}</td></tr>""");
        sb.Append("</table>");
        sb.Append($"""<p class="sub">Guardrail outcome: <b>{Esc(g.Outcome)}</b> · confidence {g.Confidence:0.0}</p>""");
        sb.Append("""<footer>Deterministic pricing — amounts computed from effective-dated master + persisted input snapshot. AI agents provide interpretation only, never amounts. Generated by GMC v2.0.</footer></body></html>""");
        return sb.ToString();
    }

    private static string Csv(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
    private static string Esc(string? s) => (s ?? "").Replace("&", "&").Replace("<", "<").Replace(">", ">");
}
