using GmcV2.Core;

namespace GmcV2.Knowledge;

/// <summary>
/// Stage 9a — resolves the effective-dated pricing master set for a case
/// (workflow §9): selects trend band by average age and IBNR band by claim count,
/// stamping the selection into VersionRef. SeedDefault mirrors Core Pricing.cs
/// defaults and the PMV-2026Q4-SEED SQL row.
/// </summary>
public sealed class MasterResolver
{
    private readonly IGmcRepository _repo;

    public MasterResolver(IGmcRepository repo) => _repo = repo;

    public async Task<PricingMasterVersion> ResolveAsync(
        decimal avgAge, int claimCount, int coveredLives, decimal largeClaimSharePct)
    {
        var master = await _repo.GetActiveMasterVersionAsync();
        var trendBand = avgAge <= 45m ? 0 : avgAge <= 60m ? 1 : 2;
        var ibnrPct = claimCount >= 300 ? 3m : claimCount >= 150 ? 5m : 8m;
        // stamp band selection for the audit trail (does not alter identity)
        master.VersionRef = $"{master.VersionRef}/trend{master.MedicalTrendByAgeBand[trendBand]:0.#}-ibnr{ibnrPct:0}";
        return master;
    }

    /// <summary>Fully-populated default master (PMV-2026Q4-SEED) — mirrors Pricing.cs + 09_seed.sql.</summary>
    public static PricingMasterVersion SeedDefault() => new()
    {
        MasterVersionId = 1,
        VersionRef = "PMV-2026Q4-SEED",
        Status = "Active",
        EffectiveFromUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        MedicalTrendByAgeBand = new[] { 8m, 9m, 10.5m },
        IbnrPctByClaimBand = 5m,
        CredibilityZ = 0.60m,
        BenchmarkBurnPerLife = 19_000m,
        RiskMarginPct = 11m,
        BaseExpensePct = 9m,
        LargeClaimNormalizationPct = 3m,
        StarRejectionPct = 2m,
        CommissionPct = 7.5m,
        GstPct = 18m,
        MaxCommissionPct = 15m,
        MaxDiscountPct = 5m,
    };
}

/// <summary>Seed taxonomy for the 40-disease master list — ICD-10 codes only from this list.</summary>
public static class DiseaseSeed
{
    public static List<DiseaseEntry> Default() => new()
    {
        new() { DiseaseName = "Type 2 Diabetes Mellitus", DiseaseGroup = "Endocrine", IcdLevel1 = "E11", IcdLevel2 = "E11.9", MatchKeywords = new[]{ "diabetes mellitus", "type 2 diabetes", "sugar" }, IsChronic = true },
        new() { DiseaseName = "Type 1 Diabetes Mellitus", DiseaseGroup = "Endocrine", IcdLevel1 = "E10", IcdLevel2 = "E10.9", MatchKeywords = new[]{ "type 1 diabetes", "juvenile diabetes" }, IsChronic = true },
        new() { DiseaseName = "Essential (Primary) Hypertension", DiseaseGroup = "Cardiovascular", IcdLevel1 = "I10", IcdLevel2 = "I10", MatchKeywords = new[]{ "hypertension", "high bp", "high blood pressure", "htn" }, IsChronic = true },
        new() { DiseaseName = "Atherosclerotic Heart Disease", DiseaseGroup = "Cardiovascular", IcdLevel1 = "I25", IcdLevel2 = "I25.1", MatchKeywords = new[]{ "coronary artery disease", "cad", "heart disease", "angina", "stent", "angioplasty" }, IsChronic = true },
        new() { DiseaseName = "Chronic Kidney Disease Stage 3", DiseaseGroup = "Renal", IcdLevel1 = "N18", IcdLevel2 = "N18.3", MatchKeywords = new[]{ "chronic kidney disease", "ckd", "renal failure" }, IsChronic = true },
        new() { DiseaseName = "Chronic Obstructive Pulmonary Disease", DiseaseGroup = "Pulmonary", IcdLevel1 = "J44", IcdLevel2 = "J44.9", MatchKeywords = new[]{ "copd", "chronic obstructive pulmonary" }, IsChronic = true },
        new() { DiseaseName = "Pulmonary Tuberculosis", DiseaseGroup = "Infection", IcdLevel1 = "A15", IcdLevel2 = "A15.0", MatchKeywords = new[]{ "tuberculosis", "tb", "ptbs" }, IsChronic = false },
        new() { DiseaseName = "Malignant Neoplasm of Breast", DiseaseGroup = "Oncology", IcdLevel1 = "C50", IcdLevel2 = "C50.9", MatchKeywords = new[]{ "carcinoma breast", "breast cancer", "ca breast" }, IsChronic = false },
        new() { DiseaseName = "Malignant Neoplasm of Prostate", DiseaseGroup = "Oncology", IcdLevel1 = "C61", IcdLevel2 = "C61", MatchKeywords = new[]{ "prostate cancer", "carcinoma prostate", "ca prostate" }, IsChronic = false },
        new() { DiseaseName = "Acute Appendicitis", DiseaseGroup = "Digestive", IcdLevel1 = "K35", IcdLevel2 = "K35.8", MatchKeywords = new[]{ "appendicitis" }, IsChronic = false },
        new() { DiseaseName = "Cholelithiasis", DiseaseGroup = "Digestive", IcdLevel1 = "K80", IcdLevel2 = "K80.2", MatchKeywords = new[]{ "gallstone", "cholelithiasis", "gall bladder stone" }, IsChronic = false },
        new() { DiseaseName = "Encounter for Full-Term Delivery", DiseaseGroup = "Maternity", IcdLevel1 = "O80", IcdLevel2 = "O80", MatchKeywords = new[]{ "normal delivery", "full-term delivery", "vaginal delivery" }, IsChronic = false },
        new() { DiseaseName = "Single Liveborn via Caesarean", DiseaseGroup = "Maternity", IcdLevel1 = "O82", IcdLevel2 = "O82", MatchKeywords = new[]{ "caesarean", "cesarean", "c-section", "lscs" }, IsChronic = false },
        new() { DiseaseName = "Gestational Diabetes", DiseaseGroup = "Maternity", IcdLevel1 = "O24", IcdLevel2 = "O24.4", MatchKeywords = new[]{ "gestational diabetes" }, IsChronic = false },
        new() { DiseaseName = "Extreme Immaturity (Preterm)", DiseaseGroup = "Neonatal", IcdLevel1 = "P07", IcdLevel2 = "P07.0", MatchKeywords = new[]{ "preterm", "premature" }, IsChronic = false },
        new() { DiseaseName = "Fracture of Neck of Femur", DiseaseGroup = "Injury", IcdLevel1 = "S72", IcdLevel2 = "S72.0", MatchKeywords = new[]{ "femur fracture", "fracture neck femur", "fracture femur" }, IsChronic = false },
        new() { DiseaseName = "Inguinal Hernia", DiseaseGroup = "Digestive", IcdLevel1 = "K40", IcdLevel2 = "K40.9", MatchKeywords = new[]{ "inguinal hernia", "hernia" }, IsChronic = false },
        new() { DiseaseName = "Senile Cataract", DiseaseGroup = "Ophthalmic", IcdLevel1 = "H25", IcdLevel2 = "H25.9", MatchKeywords = new[]{ "cataract" }, IsChronic = false },
        new() { DiseaseName = "Calculus of Kidney", DiseaseGroup = "Renal", IcdLevel1 = "N20", IcdLevel2 = "N20.0", MatchKeywords = new[]{ "kidney stone", "renal calculus", "calculus of kidney", "ureteric calculus" }, IsChronic = false },
        new() { DiseaseName = "Gastro-oesophageal Reflux / Gastritis", DiseaseGroup = "Digestive", IcdLevel1 = "K29", IcdLevel2 = "K29.7", MatchKeywords = new[]{ "gastritis", "gerd", "acid peptic" }, IsChronic = false },
        new() { DiseaseName = "Bronchial Asthma", DiseaseGroup = "Pulmonary", IcdLevel1 = "J45", IcdLevel2 = "J45.9", MatchKeywords = new[]{ "asthma", "bronchial asthma" }, IsChronic = true },
        new() { DiseaseName = "Iron Deficiency Anemia", DiseaseGroup = "Haematology", IcdLevel1 = "D50", IcdLevel2 = "D50.9", MatchKeywords = new[]{ "anemia", "anaemia", "iron deficiency" }, IsChronic = false },
        new() { DiseaseName = "Hypothyroidism", DiseaseGroup = "Endocrine", IcdLevel1 = "E03", IcdLevel2 = "E03.9", MatchKeywords = new[]{ "hypothyroid", "thyroid deficiency" }, IsChronic = true },
        new() { DiseaseName = "Hyperthyroidism", DiseaseGroup = "Endocrine", IcdLevel1 = "E05", IcdLevel2 = "E05.9", MatchKeywords = new[]{ "hyperthyroid", "thyrotoxicosis" }, IsChronic = true },
        new() { DiseaseName = "Hyperlipidemia", DiseaseGroup = "Endocrine", IcdLevel1 = "E78", IcdLevel2 = "E78.5", MatchKeywords = new[]{ "hyperlipid", "cholesterol", "dyslipidemia" }, IsChronic = true },
        new() { DiseaseName = "Obesity", DiseaseGroup = "Endocrine", IcdLevel1 = "E66", IcdLevel2 = "E66.9", MatchKeywords = new[]{ "obesity", "morbid obesity" }, IsChronic = true },
        new() { DiseaseName = "Cutaneous Abscess", DiseaseGroup = "Infection", IcdLevel1 = "L02", IcdLevel2 = "L02.9", MatchKeywords = new[]{ "abscess" }, IsChronic = false },
        new() { DiseaseName = "Acute Cholecystitis", DiseaseGroup = "Digestive", IcdLevel1 = "K81", IcdLevel2 = "K81.9", MatchKeywords = new[]{ "cholecystitis" }, IsChronic = false },
        new() { DiseaseName = "Urinary Tract Infection", DiseaseGroup = "Infection", IcdLevel1 = "N39", IcdLevel2 = "N39.0", MatchKeywords = new[]{ "uti", "urinary tract infection" }, IsChronic = false },
        new() { DiseaseName = "Pneumonia", DiseaseGroup = "Infection", IcdLevel1 = "J18", IcdLevel2 = "J18.9", MatchKeywords = new[]{ "pneumonia" }, IsChronic = false },
        new() { DiseaseName = "Gastroenteritis and Colitis", DiseaseGroup = "Digestive", IcdLevel1 = "A09", IcdLevel2 = "A09.0", MatchKeywords = new[]{ "gastroenteritis", "loose motion", "colitis" }, IsChronic = false },
        new() { DiseaseName = "Low Back Pain", DiseaseGroup = "Musculoskeletal", IcdLevel1 = "M54", IcdLevel2 = "M54.5", MatchKeywords = new[]{ "low back pain", "backache", "lumbago" }, IsChronic = false },
        new() { DiseaseName = "Concussion (Head Injury)", DiseaseGroup = "Injury", IcdLevel1 = "S06", IcdLevel2 = "S06.0", MatchKeywords = new[]{ "concussion", "head injury" }, IsChronic = false },
        new() { DiseaseName = "Angina Pectoris", DiseaseGroup = "Cardiovascular", IcdLevel1 = "I20", IcdLevel2 = "I20.0", MatchKeywords = new[]{ "angina" }, IsChronic = true },
        new() { DiseaseName = "Cerebral Infarction (Stroke)", DiseaseGroup = "Cardiovascular", IcdLevel1 = "I63", IcdLevel2 = "I63.9", MatchKeywords = new[]{ "stroke", "cerebral infarction", "brain attack" }, IsChronic = false },
        new() { DiseaseName = "Heart Failure", DiseaseGroup = "Cardiovascular", IcdLevel1 = "I50", IcdLevel2 = "I50.9", MatchKeywords = new[]{ "heart failure", "chf", "ccf" }, IsChronic = true },
        new() { DiseaseName = "Epilepsy", DiseaseGroup = "Neurology", IcdLevel1 = "G40", IcdLevel2 = "G40.9", MatchKeywords = new[]{ "epilepsy", "seizure", "fits" }, IsChronic = true },
        new() { DiseaseName = "Osteoarthritis of Knee", DiseaseGroup = "Musculoskeletal", IcdLevel1 = "M17", IcdLevel2 = "M17.9", MatchKeywords = new[]{ "osteoarthritis", "knee oa", "arthritis knee" }, IsChronic = false },
        new() { DiseaseName = "Respiratory Failure", DiseaseGroup = "Pulmonary", IcdLevel1 = "J96", IcdLevel2 = "J96.0", MatchKeywords = new[]{ "respiratory failure" }, IsChronic = false },
        new() { DiseaseName = "Syncope and Collapse", DiseaseGroup = "Neurology", IcdLevel1 = "R55", IcdLevel2 = "R55", MatchKeywords = new[]{ "syncope", "collapse", "fainting" }, IsChronic = false },
    };
}
