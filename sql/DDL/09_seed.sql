SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 09_seed.sql — shipping reference data (idempotent).
   Seeded: ref.PaidStatusDefinition, ref.AuthorityMatrix (UW 5%,
   SeniorUW 15%, ChiefUW 50% + unlimited-at-50, GST-inclusive),
   default guardrail rule set (mirrors Knowledge.GuardrailRules.
   StandardSet()), PricingMasterVersion PMV-2026Q4-SEED + PricingBand
   rows, and ~20 kb.DiseaseTaxonomy rows.
   ===================================================================== */

/* ───────────────────────── Paid status map ───────────────────────── */
INSERT INTO ref.PaidStatusDefinition (RawStatusText, NormalizedStatus)
SELECT v.Raw, v.Norm
FROM (VALUES
    /* distinct under CI collation; extra spellings covered by LIKE-style matching in code */
    (N'Paid',              N'Paid'),
    (N'Claim Paid',        N'Paid'),
    (N'Payment Done',      N'Paid'),
    (N'SETTLED',           N'Paid'),
    (N'Claim Settled',     N'Paid'),
    (N'Outstanding',       N'Outstanding'),
    (N'PENDING',           N'Outstanding'),
    (N'IN PROCESS',        N'Outstanding'),
    (N'Under Process',     N'Outstanding'),
    (N'Open',              N'Outstanding'),
    (N'Closed',            N'Closed'),
    (N'CLOSED-NO CLAIM',   N'Closed'),
    (N'Closed No Claim',   N'Closed'),
    (N'Withdrawn',         N'Closed'),
    (N'Rejected',          N'Rejected'),
    (N'DENIED',            N'Rejected'),
    (N'Declined',          N'Rejected'),
    (N'Repudiated',        N'Rejected')
) AS v(Raw, Norm)
WHERE NOT EXISTS (SELECT 1 FROM ref.PaidStatusDefinition p
                  WHERE p.RawStatusText = v.Raw
                    AND p.InsurerId IS NULL AND p.TpaId IS NULL);
GO

/* ───────────────────────── Authority matrix ─────────────────────────
   DeviationPct bands, GST-inclusive view of premium (workflow §7.3):
   Underwriter ≤ 5% deviation; SeniorUW ≤ 15%; ChiefUW ≤ 50% and
   unlimited premium at ≥ 50%. */
IF OBJECT_ID(N'uw.AuthorityMatrix', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM uw.AuthorityMatrix)
BEGIN
    INSERT INTO uw.AuthorityMatrix (AuthorityLevel, MaxPremiumAmount, MaxDeviationPct, MaxDiscountPct, CanDecline, CanWaiveException)
    VALUES
        (N'Underwriter',       5000000.00,  5.0000, 5.0000, 0, 0),
        (N'SeniorUnderwriter', 25000000.00, 15.0000, 15.0000, 1, 1),
        (N'ChiefUnderwriter',  NULL,        50.0000, 50.0000, 1, 1);
END
GO

/* ───────────────────────── Guardrail rule set ──────────────────────
   Mirror of Knowledge.GuardrailRules.StandardSet() (KNOW agent). */
IF OBJECT_ID(N'uw.GuardrailRule', N'U') IS NOT NULL
BEGIN
    INSERT INTO uw.GuardrailRule
        (RuleCode, RuleName, Category, MetricSelector, Operator, Threshold, Severity, MessageTemplate, AppliesToBusinessTypes, AppliesToExperienceBusiness, PctOfMetric, IsEnabled)
    SELECT v.RuleCode, v.RuleName, v.Category, v.MetricSelector, v.Operator, v.Threshold, v.Severity, v.MessageTemplate, v.AppliesTo, v.AppliesExp, v.PctOf, 1
    FROM (VALUES
        (N'GRD-MIN-LIVES',           N'Minimum covered lives',            N'DataSufficiency', N'CoveredLives',              N'>=', CAST(300    AS DECIMAL(18,4)), N'HardStop', N'Only {observed} covered lives; at least 300 required for credible pricing.', N'Rollover,RenewalInternal,RenewalExternal', CAST(1 AS BIT), NULL),
        (N'GRD-MIN-CLAIMS',          N'Minimum claim count',              N'DataSufficiency', N'ClaimCount',                N'>=', CAST(50     AS DECIMAL(18,4)), N'HardStop', N'Only {observed} claims in the experience period; at least 50 required.',    N'Rollover,RenewalInternal,RenewalExternal', CAST(1 AS BIT), NULL),
        (N'GRD-DQ-SCORE',            N'Minimum data quality score',       N'DataSufficiency', N'DataQualityScore',          N'>=', CAST(90     AS DECIMAL(18,4)), N'HardStop', N'Data quality score {observed} is below the floor of 90.',                   NULL,                                        CAST(0 AS BIT), NULL),
        (N'GRD-MAPPING-CONF',        N'Minimum mapping confidence',       N'DataSufficiency', N'MappingConfidenceAvg',      N'>=', CAST(90     AS DECIMAL(18,4)), N'HardStop', N'Average mapping confidence {observed} is below 90.',                        NULL,                                        CAST(0 AS BIT), NULL),
        (N'GRD-VOLATILITY',          N'Maximum claim volatility (CV)',    N'Volatility',      N'VolatilityCv',              N'<=', CAST(2.5    AS DECIMAL(18,4)), N'Refer',    N'Claim size volatility CV {observed} exceeds 2.5.',                          NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-TOP5',                N'Maximum top-5 claim share',        N'Volatility',      N'Top5ClaimSharePct',         N'<=', CAST(45     AS DECIMAL(18,4)), N'Refer',    N'Top 5 claims are {observed}% of incurred cost, above the 45% cap.',         NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-LARGE-CLAIM',         N'Maximum large-claim cost share',   N'Volatility',      N'LargeClaimSharePct',        N'<=', CAST(35     AS DECIMAL(18,4)), N'Refer',    N'Large claims (₹2L+) are {observed}% of cost, above the 35% cap.',           NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-SINGLE-LARGEST',      N'Maximum single-claim share',       N'Volatility',      N'SingleLargestSharePct',     N'<=', CAST(20     AS DECIMAL(18,4)), N'Refer',    N'The single largest claim is {observed}% of cost, above the 20% cap.',       NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-AVG-AGE',             N'Maximum average age',              N'Demographic',     N'AvgAge',                    N'<=', CAST(48     AS DECIMAL(18,4)), N'Refer',    N'Average age {observed} exceeds 48.',                                        NULL,                                        CAST(0 AS BIT), NULL),
        (N'GRD-SENIOR-SHARE',        N'Maximum 55+ population share',     N'Demographic',     N'SeniorAgeSharePct',         N'<=', CAST(30     AS DECIMAL(18,4)), N'Refer',    N'Members aged 55+ are {observed}% of lives, above the 30% cap.',             NULL,                                        CAST(0 AS BIT), NULL),
        (N'GRD-PARENT-SHARE',        N'Maximum parent share',             N'Demographic',     N'ParentSharePct',            N'<=', CAST(20     AS DECIMAL(18,4)), N'Refer',    N'Parents are {observed}% of lives, above the 20% cap.',                      NULL,                                        CAST(0 AS BIT), NULL),
        (N'GRD-CHRONIC-PREVALENCE',  N'Maximum chronic prevalence',       N'Clinical',        N'ChronicPrevalencePct',      N'<=', CAST(40     AS DECIMAL(18,4)), N'Refer',    N'Chronic prevalence {observed}% exceeds the 40% cap.',                       NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-MATERNITY-SHARE',     N'Maximum maternity cost share',     N'Clinical',        N'MaternitySharePct',         N'<=', CAST(30     AS DECIMAL(18,4)), N'Refer',    N'Maternity is {observed}% of cost, above the 30% cap.',                      NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-NON-NETWORK',         N'Maximum non-network cost share',   N'Commercial',      N'NonNetworkSharePct',        N'<=', CAST(25     AS DECIMAL(18,4)), N'Refer',    N'Non-network claims are {observed}% of cost, above the 25% cap.',            NULL,                                        CAST(1 AS BIT), NULL),
        (N'GRD-RATE-PER-LIFE',       N'Minimum technical rate per life',  N'Commercial',      N'RatePerLifeBeforeGst',      N'>=', CAST(5000   AS DECIMAL(18,4)), N'Refer',    N'Technical rate per life ₹{observed} is below the ₹5,000 floor.',            NULL,                                        CAST(0 AS BIT), NULL)
    ) AS v(RuleCode, RuleName, Category, MetricSelector, Operator, Threshold, Severity, MessageTemplate, AppliesTo, AppliesExp, PctOf)
    WHERE NOT EXISTS (SELECT 1 FROM uw.GuardrailRule g WHERE g.RuleCode = v.RuleCode);
END
GO

/* ───────────────────────── Pricing master seed ───────────────────── */
IF OBJECT_ID(N'uw.PricingMasterVersion', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM uw.PricingMasterVersion WHERE VersionLabel = N'PMV-2026Q4-SEED')
BEGIN
    DECLARE @pmv INT;
    INSERT INTO uw.PricingMasterVersion (VersionLabel, Status, EffectiveFrom, Notes)
    VALUES (N'PMV-2026Q4-SEED', N'Active', '2026-10-01', N'Shipping seed master — mirrors Core PricingMasterVersion defaults (Pricing.cs).');

    SET @pmv = SCOPE_IDENTITY();

    /* PricingBand rows — BandValueJson: {value, unit, min, max, secondaryMin, secondaryMax} */
    INSERT INTO uw.PricingBand (PricingMasterVersionId, BandType, BandKey, BandValueJson, DisplayOrder)
    VALUES
        /* MedicalTrend by average age: ≤45 → 8 / 9 / 10.5 by band */
        (@pmv, N'MedicalTrend', N'Age<=45',  N'{"value":8,"unit":"Percent","min":null,"max":45}',    1),
        (@pmv, N'MedicalTrend', N'Age46-60', N'{"value":9,"unit":"Percent","min":46,"max":60}',      2),
        (@pmv, N'MedicalTrend', N'Age>60',   N'{"value":10.5,"unit":"Percent","min":61,"max":null}', 3),
        /* Ibnr by claim count: 3 / 5 / 8, mid 5 default */
        (@pmv, N'Ibnr', N'Claims>=300',  N'{"value":3,"unit":"Percent","min":300,"max":null}', 1),
        (@pmv, N'Ibnr', N'Claims150-299',N'{"value":5,"unit":"Percent","min":150,"max":299}',  2),
        (@pmv, N'Ibnr', N'Claims<150',   N'{"value":8,"unit":"Percent","min":0,"max":149}',    3),
        (@pmv, N'Ibnr', N'Default',      N'{"value":5,"unit":"Percent"}',                      4),
        /* Credibility 0.60 */
        (@pmv, N'Credibility', N'Default', N'{"value":0.6,"unit":"Factor"}',                   1),
        /* BenchmarkBurn 19000 */
        (@pmv, N'BenchmarkBurn', N'Default', N'{"value":19000,"unit":"AmountPerLife"}',        1),
        /* RiskMargin 11 */
        (@pmv, N'RiskMargin', N'Default', N'{"value":11,"unit":"Percent"}',                     1),
        /* BaseExpense 9 */
        (@pmv, N'BaseExpense', N'Default', N'{"value":9,"unit":"Percent"}',                     1),
        /* LargeClaimNormalization 3 */
        (@pmv, N'LargeClaimNormalization', N'Default', N'{"value":3,"unit":"Percent"}',              1),
        /* StarRejection 2 */
        (@pmv, N'StarRejection', N'Default', N'{"value":2,"unit":"Percent"}',                  1),
        /* Commission 7.5 with cap 15 */
        (@pmv, N'Commission', N'Default', N'{"value":7.5,"unit":"Percent"}',                    1),
        (@pmv, N'Commission', N'Max',     N'{"value":15,"unit":"Percent"}',                     2),
        /* GST 18 */
        (@pmv, N'Gst', N'Default', N'{"value":18,"unit":"Percent"}',                           1),
        /* MaxDiscount 5 */
        (@pmv, N'MaxDiscount', N'Default', N'{"value":5,"unit":"Percent"}',                      1);

    INSERT INTO uw.CommissionMaster (PricingMasterVersionId, ChannelType, DefaultRatePct, MinRatePct, MaxRatePct)
    VALUES (@pmv, N'Default', 7.5, 0, 15);
END
GO

/* ───────────────────────── Disease taxonomy (~20 rows) ──────────────
   Mirrors the seed list the KNOW agent uses (CodingService). */
IF OBJECT_ID(N'kb.DiseaseTaxonomy', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM kb.DiseaseTaxonomy)
BEGIN
    INSERT INTO kb.DiseaseTaxonomy (StandardDiseaseName, IcdCode, DiagnosisCategory, DiseaseGroup, Department, IsChronic, BenefitGroup, DefaultLevel)
    VALUES
        (N'Type 2 Diabetes Mellitus',        N'E11.9', N'Endocrine, nutritional and metabolic diseases', N'Endocrine',   N'Endocrinology', 1, N'Other',            1),
        (N'Essential (Primary) Hypertension',N'I10',   N'Diseases of the circulatory system',            N'Cardiovascular',N'Cardiology', 1, N'Other',            1),
        (N'Atherosclerotic Heart Disease',   N'I25.1', N'Diseases of the circulatory system',            N'Cardiovascular',N'Cardiology', 1, N'Other',            1),
        (N'Hypertensive Heart Disease',      N'I11.9', N'Diseases of the circulatory system',            N'Cardiovascular',N'Cardiology', 1, N'Other',            1),
        (N'Cerebral Infarction (Stroke)',    N'I63.9', N'Diseases of the circulatory system',            N'Cardiovascular',N'Neurology',  1, N'Other',            1),
        (N'Asthma',                          N'J45.9', N'Diseases of the respiratory system',            N'Respiratory', N'Pulmonology',   1, N'Other',            1),
        (N'Chronic Obstructive Pulmonary Disease', N'J44.9', N'Diseases of the respiratory system',      N'Respiratory', N'Pulmonology',   1, N'Other',            1),
        (N'Hypothyroidism',                  N'E03.9', N'Endocrine, nutritional and metabolic diseases', N'Endocrine',   N'Endocrinology', 1, N'Other',            1),
        (N'Chronic Kidney Disease',          N'N18.9', N'Diseases of the genitourinary system',          N'KidneyUrology',N'Nephrology',  1, N'KidneyUrology',    1),
        (N'Discoid Lupus Erythematosus',     N'L93.0', N'Diseases of the skin and subcutaneous tissue',  N'Autoimmune',  N'Rheumatology',  1, N'Other',            1),
        (N'Normal Delivery',                 N'O80',   N'Pregnancy, childbirth and the puerperium',      N'Maternity',   N'Obstetrics',    0, N'Maternity',        1),
        (N'Caesarean Delivery',              N'O82',   N'Pregnancy, childbirth and the puerperium',      N'Maternity',   N'Obstetrics',    0, N'Maternity',        1),
        (N'Acute Appendicitis',              N'K35.8', N'Diseases of the digestive system',              N'Gastro',      N'General Surgery',0, N'Other',           1),
        (N'Senile Cataract',                 N'H25.9', N'Diseases of the eye and adnexa',                N'Ophthalmic',  N'Ophthalmology', 0, N'Cataract',         1),
        (N'Calculus of Kidney',              N'N20.0', N'Diseases of the genitourinary system',          N'KidneyUrology',N'Urology',     0, N'KidneyUrology',    1),
        (N'Inguinal Hernia',                 N'K40.9', N'Diseases of the digestive system',              N'Gastro',      N'General Surgery',0, N'Hernia',          1),
        (N'Pneumonia',                       N'J18.9', N'Diseases of the respiratory system',            N'Respiratory', N'Pulmonology',   0, N'Other',            1),
        (N'Dengue Fever',                    N'A91',   N'Certain infectious and parasitic diseases',     N'Fever',       N'General Medicine',0, N'Other',          1),
        (N'Intervertebral Disc Disorder',    N'M51.2', N'Diseases of the musculoskeletal system',        N'Orthopedic',  N'Orthopedics',   0, N'JointOrthopedic',  1),
        (N'Osteoarthritis of Knee',          N'M17.9', N'Diseases of the musculoskeletal system',        N'Orthopedic',  N'Orthopedics',   0, N'JointOrthopedic',  1),
        (N'Typhoid Fever',                   N'A01.0', N'Certain infectious and parasitic diseases',     N'Fever',       N'General Medicine',0, N'Other',          1),
        (N'Acute Tonsillitis',               N'J03.9', N'Diseases of the respiratory system',            N'Respiratory', N'ENT',           0, N'Other',            1);
END
GO

/* ─────────────────────── ICD reference for the seeded codes ───────── */
IF OBJECT_ID(N'kb.IcdReference', N'U') IS NOT NULL
BEGIN
    INSERT INTO kb.IcdReference (IcdCode, Description, ChapterCode)
    SELECT v.Code, v.Descr, LEFT(v.Code, 1)
    FROM (VALUES
        (N'E11.9', N'Type 2 diabetes mellitus without complications'),
        (N'I10',   N'Essential (primary) hypertension'),
        (N'I25.1', N'Atherosclerotic heart disease of native coronary artery without angina pectoris'),
        (N'I11.9', N'Hypertensive heart disease without heart failure'),
        (N'I63.9', N'Cerebral infarction, unspecified'),
        (N'J45.9', N'Asthma, unspecified'),
        (N'J44.9', N'Chronic obstructive pulmonary disease, unspecified'),
        (N'E03.9', N'Hypothyroidism, unspecified'),
        (N'N18.9', N'Chronic kidney disease, unspecified'),
        (N'L93.0', N'Discoid lupus erythematosus'),
        (N'O80',   N'Encounter for full-term uncomplicated delivery'),
        (N'O82',   N'Encounter for caesarean delivery of fetus'),
        (N'K35.8', N'Acute appendicitis, unspecified'),
        (N'H25.9', N'Senile cataract, unspecified'),
        (N'N20.0', N'Calculus of kidney'),
        (N'K40.9', N'Unilateral inguinal hernia, without obstruction or gangrene'),
        (N'J18.9', N'Pneumonia, unspecified organism'),
        (N'A91',   N'Dengue haemorrhagic fever'),
        (N'M51.2', N'Other specified intervertebral disc displacement'),
        (N'M17.9', N'Osteoarthritis of knee, unspecified'),
        (N'A01.0', N'Typhoid fever'),
        (N'J03.9', N'Acute tonsillitis, unspecified')
    ) AS v(Code, Descr)
    WHERE NOT EXISTS (SELECT 1 FROM kb.IcdReference i WHERE i.IcdCode = v.Code);
END
GO

/* ─────────────────────── Financial year 2026-27 ───────────────────── */
IF OBJECT_ID(N'ref.FinancialYear', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM ref.FinancialYear WHERE FinancialYear = N'2026-27')
BEGIN
    INSERT INTO ref.FinancialYear (FinancialYear, StartDate, EndDate, IsActive)
    VALUES (N'2026-27', '2026-04-01', '2027-03-31', 1);
END
GO

/* ─────────────────────── Roles + core permissions ─────────────────── */
IF OBJECT_ID(N'ref.Role', N'U') IS NOT NULL
BEGIN
    INSERT INTO ref.Role (RoleName, Description)
    SELECT v.RN, v.D
    FROM (VALUES
        (N'Admin',             N'Platform administrator'),
        (N'SalesPerson',       N'Sales workspace user'),
        (N'Underwriter',       N'Underwriter'),
        (N'SeniorUnderwriter', N'Senior underwriter — 15% deviation band'),
        (N'ChiefUnderwriter',  N'Chief underwriter — unlimited band'),
        (N'Actuarial',         N'Pricing masters owner'),
        (N'Auditor',           N'Read-only audit')
    ) AS v(RN, D)
    WHERE NOT EXISTS (SELECT 1 FROM ref.Role r WHERE r.RoleName = v.RN);
END
GO

/* ─────────────────────── Case number seq per FY ───────────────────── */
IF OBJECT_ID(N'core.CaseNumberSeq', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM core.CaseNumberSeq WHERE FinancialYear = N'2627')
BEGIN
    INSERT INTO core.CaseNumberSeq (FinancialYear, LastSeq) VALUES (N'2627', 0);
END
GO

PRINT '09_seed done';
GO
