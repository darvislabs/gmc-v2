using GmcV2.Core;
using System.Text.Json;

namespace GmcV2.Web;

/// <summary>Excel masters upload (prototype §7 "UW Master Structure"): admin uploads a
/// masters workbook (xlsx/csv) whose named sheets map to master tables — BenefitCosting,
/// StandardLoading, Commission. Deterministic upsert per master version.</summary>
public static class MastersUploadApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/admin/masters/upload", async (HttpContext ctx, IGmcRepository repo) =>
        {
            if (!string.Equals(ctx.Session.GetString("role"), "Admin", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(403);
            var file = ctx.Request.Form.Files["file"];
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "no file part" });
            if (file.Length > 20 * 1024 * 1024) return Results.BadRequest(new { error = "file exceeds 20 MB" });

            var parsed = GmcV2.Infrastructure.FileParser.Parse(file.FileName, ReadAll(ctx.Request.Form.Files["file"]));
            if (parsed.Error is not null) return Results.BadRequest(new { error = parsed.Error });

            var counts = new Dictionary<string, int>();
            var version = await repo.GetActiveMasterVersionAsync();

            foreach (var sheet in parsed.Sheets)
            {
                var name = sheet.SheetName.Trim();
                // Flat CSVs come in as "Sheet1" — sniff the headers to route instead of
                // relying on the sheet name alone.
                var h = sheet.Headers.Select(x => x.Trim().Replace(" ", "")).ToList();
                var routed = name.ToLowerInvariant() switch
                {
                    "benefitcosting" or "benefit costing" => "benefitcosting",
                    "commission" => "commission",
                    "standardloading" or "standard loading" or "loadings" => "standardloading",
                    _ when h.Any(x => x.Equals("BenefitCode", StringComparison.OrdinalIgnoreCase)) => "benefitcosting",
                    _ when h.Any(x => x.Equals("ChannelType", StringComparison.OrdinalIgnoreCase) || x.Equals("Channel", StringComparison.OrdinalIgnoreCase)) => "commission",
                    _ when h.Any(x => x.Equals("LoadingName", StringComparison.OrdinalIgnoreCase) || x.Equals("Loading", StringComparison.OrdinalIgnoreCase)) => "standardloading",
                    _ => "",
                };
                var n = routed switch
                {
                    "benefitcosting" => await UpsertBenefitCostingAsync(repo, version.MasterVersionId, sheet),
                    "commission" => await UpsertCommissionAsync(repo, version.MasterVersionId, sheet),
                    "standardloading" => await UpsertStandardLoadingAsync(repo, version.MasterVersionId, sheet),
                    _ => -1,
                };
                counts[routed.Length > 0 ? routed : name] = n;
            }
            return Results.Json(new
            {
                ok = true,
                fileName = file.FileName,
                masterVersion = version.VersionRef,
                sheets = counts,
                note = "Unknown sheets are skipped (-1). Master rows apply on the next pricing run.",
            });
        });
    }

    private static byte[] ReadAll(IFormFile f) { using var ms = new MemoryStream(); f.CopyTo(ms); return ms.ToArray(); }

    private static int Col(string[] headers, params string[] names)
    {
        foreach (var want in names)
        {
            var i = Array.FindIndex(headers, h => h.Trim().Replace(" ", "").Equals(want.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return i;
        }
        return -1;
    }
    private static string At(string[] cells, int i) => i >= 0 && i < cells.Length ? (cells[i] ?? "").Trim() : "";
    private static decimal? Dec(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return decimal.TryParse(s.Replace("₹", "").Replace(",", "").Replace("%", "").Trim(), out var d) ? d : null;
    }

    private static async Task<int> UpsertBenefitCostingAsync(IGmcRepository repo, int masterVersionId, ParsedSheet sheet)
    {
        var h = sheet.Headers.ToArray();
        var cCode = Col(h, "BenefitCode", "Benefit", "Code");
        if (cCode < 0) return -1;
        var cUtil = Col(h, "UtilizationRate", "Utilization");
        var cSev = Col(h, "SeverityCaptureRate", "SeverityCapture");
        var cInc = Col(h, "IncidenceRate", "Incidence");
        var cAmt = Col(h, "SeverityAmount", "Severity");
        var cFactor = Col(h, "FactorPct", "Factor");
        var cCred = Col(h, "ExperienceCredibilityPct", "Credibility");
        var cAttr = Col(h, "AttributionFactor", "Attribution");
        var n = 0;
        foreach (var r in sheet.Rows)
        {
            var code = At(r, cCode);
            if (code.Length == 0) continue;
            await repo.UpsertBenefitCostingAsync(new BenefitCostingRow
            {
                MasterVersionId = masterVersionId, BenefitCode = code,
                UtilizationRate = Dec(At(r, cUtil)) ?? 0m,
                SeverityCaptureRate = Dec(At(r, cSev)) ?? 0m,
                IncidenceRate = Dec(At(r, cInc)) ?? 0m,
                SeverityAmount = Dec(At(r, cAmt)) ?? 0m,
                FactorPct = Dec(At(r, cFactor)) ?? 0m,
                ExperienceCredibilityPct = Dec(At(r, cCred)) ?? 0m,
                AttributionFactor = Dec(At(r, cAttr)) ?? 1m,
            });
            n++;
        }
        return n;
    }

    private static async Task<int> UpsertCommissionAsync(IGmcRepository repo, int masterVersionId, ParsedSheet sheet)
    {
        var h = sheet.Headers.ToArray();
        var cChan = Col(h, "ChannelType", "Channel");
        if (cChan < 0) return -1;
        var cDef = Col(h, "DefaultRatePct", "Default");
        var cMin = Col(h, "MinRatePct", "Min");
        var cMax = Col(h, "MaxRatePct", "Max");
        var n = 0;
        foreach (var r in sheet.Rows)
        {
            var ch = At(r, cChan);
            if (ch.Length == 0) continue;
            await repo.UpsertCommissionAsync(new CommissionRow
            {
                MasterVersionId = masterVersionId, ChannelType = ch,
                DefaultRatePct = Dec(At(r, cDef)) ?? 7.5m,
                MinRatePct = Dec(At(r, cMin)) ?? 0m,
                MaxRatePct = Dec(At(r, cMax)) ?? 15m,
            });
            n++;
        }
        return n;
    }

    private static async Task<int> UpsertStandardLoadingAsync(IGmcRepository repo, int masterVersionId, ParsedSheet sheet)
    {
        var h = sheet.Headers.ToArray();
        var cName = Col(h, "LoadingName", "Loading", "Name");
        if (cName < 0) return -1;
        var cVal = Col(h, "Value", "Pct", "Rate", "FactorPct");
        var n = 0;
        foreach (var r in sheet.Rows)
        {
            var nm = At(r, cName);
            if (nm.Length == 0) continue;
            await repo.UpsertStandardLoadingAsync(new StandardLoadingRow
            {
                MasterVersionId = masterVersionId, LoadingName = nm,
                Value = Dec(At(r, cVal)) ?? 0m,
            });
            n++;
        }
        return n;
    }
}
