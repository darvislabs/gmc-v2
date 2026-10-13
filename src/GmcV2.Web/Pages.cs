using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>Placeholder console routes — replaced wholesale by the UI build agent (agent UIX).</summary>
public static class Pages
{
    public static void MapAll(WebApplication app)
    {
        app.MapGet("/", (IGmcRepository repo) => Results.Content(
            """<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GMC v2</title></head><body style="font-family:system-ui;background:#080808;color:#FCFCFC;display:flex;min-height:100vh;align-items:center;justify-content:center"><div style="text-align:center"><h1 style="font-weight:700">GMC v2 console — rebuilding</h1><p style="opacity:.6">The full Opexminds console is being assembled. API is fully live at /api/v1.</p></div></body></html>""",
            "text/html; charset=utf-8"));
    }
}
