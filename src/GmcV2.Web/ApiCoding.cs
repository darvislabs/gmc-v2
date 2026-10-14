using GmcV2.Core;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>
/// Clinical coding API — backs the #/coding workspace (tech §6 Clinical Review).
/// GET  /api/v1/cases/{id}/coding[?status=Unmapped|InReview|...]  → current codings joined to StdClaims
/// POST /api/v1/cases/{id}/coding/{codingId}/review               → human decision (versioned, audited, golden)
/// </summary>
public static class ApiCoding
{
    public static void MapAll(WebApplication app)
    {
        app.MapGet("/api/v1/cases/{id:long}/coding", async (long id, IGmcRepository repo, string? status) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            var codings = await repo.GetClaimCodingAsync(id, status);
            var claims = StandardizedClaims.For(id).ToDictionary(x => x.StdClaimId);

            var rows = codings.Select(cc =>
            {
                claims.TryGetValue(cc.StdClaimId, out var sc);
                return new
                {
                    claimCodingId = cc.ClaimCodingId,
                    stdClaimId = cc.StdClaimId,
                    claimNumber = cc.ClaimNumber ?? sc?.ClaimNumber ?? "",
                    memberNumber = sc?.MemberNumber ?? "",
                    status = sc?.ClaimStatus ?? "",
                    incurred = sc?.IncurredAmount ?? 0m,
                    isLarge = sc?.IsLargeClaim ?? false,
                    diagnosisText = sc?.DiseaseCategory ?? "",
                    level1Icd = cc.Level1IcdCode,
                    level1Name = cc.Level1DiseaseName,
                    level1Confidence = cc.Level1Confidence,
                    level1Provenance = cc.Level1Provenance,
                    diseaseGroup = cc.DiseaseGroup,
                    benefitGroup = cc.BenefitGroup,
                    isChronic = cc.IsChronic,
                    codingStatus = cc.CodingStatus,
                    needsHumanReview = cc.NeedsHumanReview,
                    reason = cc.ReasonText,
                    codingVersion = cc.CodingVersion,
                };
            })
            // Synthetic ids can outlive their StandardizedClaims slot (process restart with SQL codings):
            // drop orphans rather than rendering rows without amounts.
            .Where(r => r.incurred > 0m || claims.ContainsKey(r.stdClaimId))
            .ToList();

            var open = (await repo.GetOpenExceptionsAsync(id)).ToList();
            var unmappedException = open.FirstOrDefault(e => e.Code == Enums.ExcClinUnmapped);
            var reviewException = open.FirstOrDefault(e => e.Code == Enums.ExcClinReview);
            return Results.Json(new
            {
                rows,
                counts = new
                {
                    total = rows.Count,
                    unmapped = rows.Count(r => r.codingStatus == "Unmapped"),
                    inReview = rows.Count(r => r.codingStatus == "InReview"),
                    autoAccepted = rows.Count(r => r.codingStatus == "AutoAccepted"),
                    humanSettled = rows.Count(r => r.codingStatus is "HumanAccepted" or "HumanCorrected"),
                },
                exceptions = new
                {
                    unmapped = unmappedException?.ExceptionId,
                    unmappedResolved = unmappedException is null,
                    review = reviewException?.ExceptionId,
                    reviewResolved = reviewException is null,
                },
            });
        });

        app.MapPost("/api/v1/cases/{id:long}/coding/{codingId:long}/review", async (long id, long codingId, ReviewDto dto, IGmcRepository repo, HttpContext ctx) =>
        {
            if (await repo.GetCaseAsync(id) is null) return Results.NotFound(new { error = "case not found" });
            var valid = new[] { "Accepted", "Corrected", "Rejected", "MarkedUncertain", "NeedMoreInfo" };
            if (!valid.Contains(dto.Decision, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"decision must be one of {string.Join(", ", valid)}" });
            if (dto.Decision.Equals("Corrected", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(dto.IcdLevel1) || string.IsNullOrWhiteSpace(dto.DiseaseName)))
                return Results.BadRequest(new { error = "corrected decisions require icdLevel1 + diseaseName" });

            var current = (await repo.GetClaimCodingAsync(id)).FirstOrDefault(x => x.ClaimCodingId == codingId);
            if (current is null) return Results.NotFound(new { error = "coding row not found" });

            var reviewer = ctx.Session.GetString("email") ?? "reviewer";
            var accepted = dto.Decision.Equals("Accepted", StringComparison.OrdinalIgnoreCase);
            var corrected = dto.Decision.Equals("Corrected", StringComparison.OrdinalIgnoreCase);

            // New version per db §6.1 — the human-settled row supersedes the machine row.
            var next = new ClaimCoding
            {
                StdClaimId = current.StdClaimId,
                CaseId = id,
                ClaimNumber = current.ClaimNumber,
                Level1IcdCode = corrected ? dto.IcdLevel1!.Trim() : current.Level1IcdCode,
                Level1DiseaseName = corrected ? dto.DiseaseName!.Trim() : current.Level1DiseaseName,
                Level1Confidence = accepted || corrected ? 1.00m : current.Level1Confidence,
                Level1Provenance = "Manual",
                Level2IcdCode = current.Level2IcdCode,
                Level2DiseaseName = current.Level2DiseaseName,
                DiseaseGroup = current.DiseaseGroup,
                IsChronic = current.IsChronic,
                BenefitGroup = current.BenefitGroup,
                MaternityDeliveryType = current.MaternityDeliveryType,
                NeedsHumanReview = false,
                CodingStatus = corrected ? "HumanCorrected" : accepted ? "HumanAccepted" : current.CodingStatus,
                GoldenEntryId = current.GoldenEntryId,
                ReasonText = dto.Comments is { Length: > 0 } ? $"human {dto.Decision.ToLowerInvariant()}: {dto.Comments}" : $"human {dto.Decision.ToLowerInvariant()}",
            };

            // Correction → golden dataset entry (text that the human confirmed maps to this ICD).
            var diagnosisText = StandardizedClaims.For(id).FirstOrDefault(x => x.StdClaimId == current.StdClaimId)?.DiseaseCategory;
            CodingReview review;
            if (diagnosisText is { Length: > 0 })
            {
                var golden = await repo.GetGoldenEntriesAsync();
                var norm = KnowledgeNorm(diagnosisText);
                var hit = golden.FirstOrDefault(g => g.RawTextNorm.Replace(" ", "") == norm.Replace(" ", ""));
                if (hit is not null)
                {
                    await repo.IncrementGoldenUseAsync(hit.GoldenEntryId);
                    next.GoldenEntryId = hit.GoldenEntryId;
                }
                else if (corrected)
                {
                    await repo.AddGoldenEntryAsync(new GoldenEntry
                    {
                        RawTextNorm = norm,
                        IcdLevel1 = next.Level1IcdCode!,
                        IcdLevel2 = next.Level2IcdCode,
                        WasCorrected = true,
                        Source = "ReviewCorrection",
                    });
                    var created = (await repo.GetGoldenEntriesAsync()).FirstOrDefault(g => g.RawTextNorm.Replace(" ", "") == norm.Replace(" ", ""));
                    next.GoldenEntryId = created?.GoldenEntryId;
                }
            }

            next = await repo.SaveClaimCodingAsync(next);
            review = new CodingReview
            {
                ClaimCodingId = next.ClaimCodingId,
                Reviewer = reviewer,
                Decision = dto.Decision,
                SuggestedLevel1Json = System.Text.Json.JsonSerializer.Serialize(new { current.Level1IcdCode, current.Level1DiseaseName, current.Level1Confidence }),
                FinalLevel1Json = System.Text.Json.JsonSerializer.Serialize(new { next.Level1IcdCode, next.Level1DiseaseName, next.Level1Confidence }),
                Comments = dto.Comments,
                PromotedToGolden = corrected,
            };
            await repo.AddCodingReviewAsync(review);
            await repo.AddNoteAsync(id, reviewer, $"Coding {next.ClaimNumber ?? next.ClaimCodingId.ToString()} — {dto.Decision}"
                + (corrected ? $" → {next.Level1IcdCode} {next.Level1DiseaseName}" : "") + (string.IsNullOrWhiteSpace(dto.Comments) ? "" : $" ({dto.Comments})"));

            // Auto-resolve the parked clinical exceptions once the queue drains.
            var remaining = await repo.GetClaimCodingAsync(id);
            var unmappedLeft = remaining.Count(x => x.CodingStatus is "Unmapped" or "InReview" && x.NeedsHumanReview);
            if (unmappedLeft == 0)
            {
                foreach (var e in await repo.GetOpenExceptionsAsync(id))
                    if (e.Code is Enums.ExcClinUnmapped or Enums.ExcClinReview)
                        await repo.ResolveExceptionAsync(e.ExceptionId, reviewer);
            }

            return Results.Json(new
            {
                ok = true,
                coding = next,
                remainingReview = unmappedLeft,
                exceptionsResolved = unmappedLeft == 0,
                goldenPromoted = corrected && next.GoldenEntryId is not null,
            });
        });
    }

    private static string KnowledgeNorm(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(ch);
        return sb.ToString().Trim();
    }

    internal record ReviewDto(string Decision, string? IcdLevel1, string? DiseaseName, string? Comments);
}
