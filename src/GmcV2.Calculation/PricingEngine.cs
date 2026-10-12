using GmcV2.Core;

namespace GmcV2.Calculation;

/// <summary>
/// Stage 9 — the deterministic pricing chain (workflow §9, tech §12). Every step is a
/// PricingComponent: observed burn → large-claim normalization → star rejection + IBNR
/// → ultimate burn → credibility blend → medical trend → projected burn → benefit delta
/// → final burn → net technical → gross-up → loadings/discount → technical premium
/// → commercial discount → premium before GST → GST 18% → total.
/// Overrides replace inputs, never formulas (workflow §7.2) and REQUIRE a reason.
/// Bridge-compatible: static Compute(snap, master, delta[, overrides]).
/// </summary>
public sealed class PricingEngine
{
    public PricingEngine() { }

    private static decimal R100(decimal x) =>
        Math.Round(x / 100m, 0, MidpointRounding.AwayFromZero) * 100m;

    private static decimal R2(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    public static PricingResult Compute(AnalyticsSnapshot snap, PricingMasterVersion master,
        BenefitDeltaResult delta, PricingOverrides? overrides = null)
    {
        var ov = overrides ?? new PricingOverrides();
        if (ov.HasAny && string.IsNullOrWhiteSpace(ov.Reason))
            throw new ArgumentException("Every override set requires a mandatory reason (workflow §7.2).");

        var components = new List<PricingComponent>();
        var step = 0;
        void Add(string name, string src, decimal value, string unit, string? basis = null,
            bool isOverride = false, string? reason = null)
            => components.Add(new PricingComponent
            {
                StepOrder = ++step,
                ComponentName = name,
                SourceType = src,
                MasterVersionRef = master.VersionRef,
                Value = value,
                Unit = unit,
                Basis = basis,
                IsOverride = isOverride,
                OverrideReason = reason,
            });

        var lives = Math.Max(snap.CoveredLives, 1);

        // 1 — observed burn
        var observed = snap.BurnCostPerLife > 0 ? snap.BurnCostPerLife : snap.AmountIncurred / lives;
        Add("Observed burn per life", Enums.SrcComputed, R2(observed), "₹/life",
            $"incurred {snap.AmountIncurred:0} over {lives} lives");

        // 2 — large-claim normalization (band on share of cost from ₹2L+ claims)
        var share = snap.LargeClaimSharePct;
        var lcn = ov.LargeClaimNormalizationPct ?? (share <= 15m ? 0m : share <= 30m ? master.LargeClaimNormalizationPct : 7m);
        Add("Large-claim normalization", Enums.SrcMaster, lcn, "%",
            $"large-claim share {share:0.##}% — band {(share <= 15m ? "≤15: none" : share <= 30m ? "16-30: master" : ">30: heavy")}",
            ov.LargeClaimNormalizationPct is not null, ov.Reason);

        // 3 — normalized burn
        var normalized = observed * (1 - lcn / 100m);
        Add("Normalized burn per life", Enums.SrcComputed, R2(normalized), "₹/life", $"−{lcn:0.##}% normalization");

        // 4 — star rejection (tpa/insurer pending-rejection deduction)
        var star = ov.StarRejectionPct ?? master.StarRejectionPct;
        Add("Star rejection", Enums.SrcMaster, star, "%", "master default", ov.StarRejectionPct is not null, ov.Reason);

        // 5 — IBNR by claim-count band (≥300 → 3, ≥150 → 5, else 8)
        var ibnr = ov.IbnrPct ?? (snap.ClaimCount >= 300 ? 3m : snap.ClaimCount >= 150 ? 5m : 8m);
        Add("IBNR", Enums.SrcMaster, ibnr, "%", $"claim count {snap.ClaimCount} — band {(snap.ClaimCount >= 300 ? "≥300: 3" : snap.ClaimCount >= 150 ? "150-299: 5" : "<150: 8")}",
            ov.IbnrPct is not null, ov.Reason);

        // 6 — ultimate burn (§9: less star rejection, plus IBNR — additive on the normalized burn)
        var ultimate = normalized * (1 - star / 100m) + normalized * (ibnr / 100m);
        Add("Ultimate burn per life", Enums.SrcComputed, R2(ultimate), "₹/life", $"−{star:0.##}% star +{ibnr:0.##}% IBNR");

        // 7 — credibility blend with portfolio benchmark
        var z = ov.CredibilityZ ?? master.CredibilityZ;
        var benchmark = ov.BenchmarkBurnPerLife ?? master.BenchmarkBurnPerLife;
        var credible = ultimate * z + benchmark * (1 - z);
        Add("Credibility blend", Enums.SrcMaster, R2(credible), "₹/life",
            $"Z={z:0.##} × own experience + {1 - z:0.##} × benchmark ₹{benchmark:0}",
            ov.CredibilityZ is not null || ov.BenchmarkBurnPerLife is not null, ov.Reason);

        // 8 — medical trend by average-age band (≤45 → idx0, 46-60 → idx1, >60 → idx2)
        var avgAge = snap.AvgAge;
        var trend = ov.MedicalTrendPct ?? (avgAge <= 45m ? master.MedicalTrendByAgeBand[0]
            : avgAge <= 60m ? master.MedicalTrendByAgeBand[1] : master.MedicalTrendByAgeBand[2]);
        Add("Medical trend", Enums.SrcMaster, trend, "%", $"average age {avgAge:0.#} — band {(avgAge <= 45m ? "≤45" : avgAge <= 60m ? "46-60" : "60+")}",
            ov.MedicalTrendPct is not null, ov.Reason);

        // 9 — projected burn
        var projected = credible * (1 + trend / 100m);
        Add("Projected burn per life", Enums.SrcComputed, R2(projected), "₹/life", $"+{trend:0.##}% trend");

        // 10 — benefit delta
        var deltaPerLife = delta?.TotalDeltaPerLife ?? 0m;
        Add("Benefit delta per life", Enums.SrcBenefitDelta, R2(deltaPerLife), "₹/life",
            delta is { Lines.Count: > 0 } ? string.Join("; ", delta.Lines.Select(l => $"{l.BenefitName} {l.DeltaPerLife:0}")) : "no benefit changes");

        // 11 — final projected burn
        var final = projected + deltaPerLife;
        Add("Final projected burn per life", Enums.SrcComputed, R2(final), "₹/life");

        // 12 — net technical premium
        var netTechnical = R100(final * lives);
        Add("Net technical premium", Enums.SrcComputed, netTechnical, "₹", $"× {lives} lives, rounded to ₹100");

        // 13 — gross-up (expense by lives, risk margin by large-claim share, commission master)
        var expense = ov.BaseExpensePct ?? (lives >= 1000 ? 7.5m : lives >= 500 ? 9m : 10.5m);
        var margin = ov.RiskMarginPct ?? (share <= 15m ? 8m : share <= 30m ? master.RiskMarginPct : 15m);
        var commission = ov.CommissionPct ?? master.CommissionPct;
        var grossUpPct = expense + margin + commission;
        Add("Gross-up", Enums.SrcMaster, grossUpPct, "%", $"expense {expense:0.#} + risk margin {margin:0.#} + commission {commission:0.#}",
            ov.BaseExpensePct is not null || ov.RiskMarginPct is not null || ov.CommissionPct is not null, ov.Reason);

        // 14 — grossed-up net
        var grossedUp = netTechnical * (1 + grossUpPct / 100m);
        Add("Grossed-up premium", Enums.SrcComputed, R2(grossedUp), "₹", $"+{grossUpPct:0.##}%");

        // 15 — manual loading (override-only step; shown when set)
        var load = ov.ManualLoadingPct ?? 0m;
        var afterLoad = grossedUp * (1 + load / 100m);
        if (load != 0m)
            Add("Manual loading", Enums.SrcOverride, load, "%", ov.Reason, true, ov.Reason);

        // 16 — technical premium
        var technical = R100(afterLoad);
        if (technical <= 0) throw new InvalidOperationException("Pricing produced a non-positive technical premium.");
        Add("Technical premium", Enums.SrcComputed, technical, "₹", "rounded to ₹100");

        // 17 — approved commercial discount (never deeper than the master cap)
        var discount = ov.ManualDiscountPct ?? 0m;
        var commercial = R100(technical * (discount / 100m));
        var beforeGst = technical - commercial;
        if (commercial > 0)
        {
            if (discount > master.MaxDiscountPct)
                throw new InvalidOperationException($"Commercial discount {discount:0.#}% exceeds the master cap of {master.MaxDiscountPct:0.#}%.");
            Add("Approved commercial discount", Enums.SrcOverride, -commercial, "₹", ov.Reason, true, ov.Reason);
        }
        Add("Premium before GST", Enums.SrcComputed, beforeGst, "₹");

        // 19 — GST and total
        var gst = R100(beforeGst * master.GstPct / 100m);
        Add($"GST at {master.GstPct:0.#}%", Enums.SrcMaster, gst, "₹", $"{master.GstPct:0.#}% of premium before GST");
        var total = beforeGst + gst;
        Add("Total premium (incl. GST)", Enums.SrcComputed, total, "₹");

        return new PricingResult
        {
            MasterVersionId = master.MasterVersionId,
            MasterVersionRef = master.VersionRef,
            Components = components,
            ObservedBurnPerLife = R2(observed),
            NormalizedBurnPerLife = R2(normalized),
            UltimateBurnPerLife = R2(ultimate),
            TrendedBurnPerLife = R2(projected),
            BenefitDeltaPerLife = R2(deltaPerLife),
            NetTechnicalPerLife = R2(final),
            TechnicalPremium = technical,
            GrossPremium = beforeGst,
            GrossPremiumPerLife = R2(beforeGst / lives),
            LoadingsPerLife = R2(technical / lives - final),
            InputSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                snap.CoveredLives, snap.ClaimCount, snap.AmountIncurred, snap.AvgAge,
                snap.LargeClaimSharePct, snap.VolatilityCv, DeltaPerLife = R2(deltaPerLife),
            }),
            InputsHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{snap.AmountIncurred}|{snap.CoveredLives}|{R2(deltaPerLife)}|{master.VersionRef}|{grossUpPct}|{trend}|{ibnr}|{star}|{lcn}|{z}|{benchmark}|{load}|{discount}")))[..32],
            ComputedAtUtc = DateTime.UtcNow,
        };
    }
}
