using GmcV2.Core;

namespace GmcV2.Web;

/// <summary>
/// Commercial discount loop (prototype v74 step 8): Sales requests a discount against the
/// current quote; UW chain approves/declines per uw.AuthorityMatrix (UW 5% / Senior 15% /
/// Chief 50%). Approval mints a new superseding quote version with the approved discount,
/// recorded via the §11 override chain — same versioned, reason-captured discipline.
/// </summary>
public static class DiscountApi
{
    public sealed record CreateDto(decimal RequestedPct, string? Remarks, string? JustificationType);
    public sealed record DecideDto(decimal? ApprovedPct, string Remarks, long? ResultingQuoteVersionId);

    public static void Map(WebApplication app)
    {
        // list requests for a case (visible to all roles on the case page)
        app.MapGet("/api/v1/cases/{id:long}/discounts", async (long id, IGmcRepository repo) =>
            Results.Json(await repo.GetDiscountRequestsAsync(id)));

        // sales (or any role) requests a discount on the current quote
        app.MapPost("/api/v1/cases/{id:long}/discounts", async (long id, CreateDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (dto.RequestedPct is < 0 or > 50) return Results.BadRequest(new { error = "requested % must be 0–50" });
            var quotes = await repo.GetQuoteVersionsAsync(id);
            var current = quotes.FirstOrDefault(q => q.Status == "Current") ?? quotes.LastOrDefault();
            if (current is null) return Results.BadRequest(new { error = "no quote on this case yet — run the pipeline first" });

            var email = ctx.Session.GetString("email") ?? "sales";
            var userId = int.TryParse(ctx.Session.GetString("userId"), out var uid) ? uid : 0;
            var row = await repo.SaveDiscountRequestAsync(new DiscountRequestRow
            {
                CaseId = id,
                QuoteVersionId = current.QuoteVersionId,
                RequestedByUserId = userId,
                RequestedByEmail = email,
                RequestedPct = dto.RequestedPct,
                RequestedRemarks = dto.Remarks,
                JustificationType = dto.JustificationType ?? "CommercialDiscount",
                Status = DiscountRequestRow.StRequested,
            });
            await repo.AddNoteAsync(id, "Discount",
                $"Discount {dto.RequestedPct}% requested on V{current.VersionNo} by {email}.");
            return Results.Json(row);
        });

        // decision: uw/senior/vp/admin per authority matrix
        app.MapPost("/api/v1/discounts/{id:long}/decide", async (long id, DecideDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            var req = await repo.GetDiscountRequestAsync(id);
            if (req is null) return Results.NotFound(new { error = "discount request not found" });
            if (req.Status != DiscountRequestRow.StRequested) return Results.Conflict(new { error = $"already {req.Status}" });

            var email = ctx.Session.GetString("email") ?? "";
            var role = ctx.Session.GetString("role") ?? "Sales";
            var approvePct = dto.ApprovedPct ?? 0m;
            var allowed = role switch
            {
                "Underwriter" => 5m,
                "SeniorUnderwriter" => 15m,
                "ChiefUnderwriter" or "VP" or "Admin" => 50m,
                _ => 0m, // sales cannot decide
            };
            if (allowed <= 0) return Results.StatusCode(403);
            if (approvePct > req.RequestedPct) return Results.BadRequest(new { error = $"cannot approve more than requested ({req.RequestedPct}%)" });
            if (approvePct > allowed) return Results.StatusCode(403);
            var approverEmail = email;
            var approverId = int.TryParse(ctx.Session.GetString("userId"), out var aid) ? aid : 0;

            if (dto.ApprovedPct is null || approvePct <= 0)
            {
                req.Status = DiscountRequestRow.StDeclined;
                req.ApprovedByUserId = approverId;
                req.ApprovedByEmail = approverEmail;
                req.ApprovalRemarks = dto.Remarks;
            }
            else
            {
                req.Status = DiscountRequestRow.StApproved;
                req.ApprovedPct = approvePct;
                req.ApprovedByUserId = approverId;
                req.ApprovedByEmail = approverEmail;
                req.ApprovalRemarks = dto.Remarks;
                req.ResultingQuoteVersionId = dto.ResultingQuoteVersionId;
            }
            req.DecidedAtUtc = DateTime.UtcNow;
            await repo.SaveDiscountRequestAsync(req);
            await repo.AddNoteAsync(req.CaseId, "Discount",
                $"Discount request #{id} {req.Status.ToLower()} at {approvePct}% by {email}.");
            return Results.Json(req);
        });
    }
}
