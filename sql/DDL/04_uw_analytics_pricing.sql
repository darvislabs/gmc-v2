SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 04_uw_analytics_pricing.sql — analytics snapshots, pricing
   masters and pricing runs (db.md §8.1–8.2, §9.1–9.10). Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'uw.AnalyticsSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE uw.AnalyticsSnapshot(
        AnalyticsSnapshotId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_AnalyticsSnapshot PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        CaseStageRunId   BIGINT        NULL,
        SnapshotVersion  INT           NOT NULL CONSTRAINT DF_Snap_Version DEFAULT (1),
        IsCurrent        BIT           NOT NULL CONSTRAINT DF_Snap_IsCurrent DEFAULT (1),
        CoveredLives     INT           NOT NULL CONSTRAINT DF_Snap_Lives DEFAULT (0),
        EmployeeCount    INT           NOT NULL CONSTRAINT DF_Snap_Employees DEFAULT (0),
        OpeningLives     INT           NULL,
        ClosingLives     INT           NULL,
        AverageLives     INT           NULL,
        PolicyServiceDays INT          NULL,
        ClaimCount       INT           NOT NULL CONSTRAINT DF_Snap_Claims DEFAULT (0),
        PaidClaimCount   INT           NOT NULL CONSTRAINT DF_Snap_PaidClaims DEFAULT (0),
        OutstandingClaimCount INT      NOT NULL CONSTRAINT DF_Snap_OutstandingClaims DEFAULT (0),
        RejectedClaimCount INT         NOT NULL CONSTRAINT DF_Snap_RejectedClaims DEFAULT (0),
        TotalClaimedAmount  DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Claimed DEFAULT (0),
        TotalApprovedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Approved DEFAULT (0),
        TotalSanctionedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Sanctioned DEFAULT (0),
        TotalPaidAmount  DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Paid DEFAULT (0),
        TotalOutstandingAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Outstanding DEFAULT (0),
        TotalIncurredAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Incurred DEFAULT (0),
        AverageClaimAmount DECIMAL(18,2) NULL,
        MedianClaimAmount DECIMAL(18,2) NULL,
        ClaimFrequencyPer100 DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Freq DEFAULT (0),
        ClaimantIncidencePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Incidence DEFAULT (0),
        ClaimCostPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_CostPerLife DEFAULT (0),
        ClaimCostPerEmployee DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_CostPerEmployee DEFAULT (0),
        BurnCostPerLife  DECIMAL(18,2) NOT NULL CONSTRAINT DF_Snap_Burn DEFAULT (0),
        InceptionPremium DECIMAL(18,2) NULL,
        FinalYearPremium DECIMAL(18,2) NULL,
        EarnedPremium    DECIMAL(18,2) NULL,
        LossRatioPct     DECIMAL(9,4)  NULL,
        AverageAge       DECIMAL(6,2)  NOT NULL CONSTRAINT DF_Snap_AvgAge DEFAULT (0),
        Age55PlusSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Age55 DEFAULT (0),
        DependentSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Dependent DEFAULT (0),
        ParentSharePct   DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Snap_Parent DEFAULT (0),
        ChronicPrevalencePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Chronic DEFAULT (0),
        MaternityClaimCount INT        NOT NULL CONSTRAINT DF_Snap_MatCount DEFAULT (0),
        MaternityNormalCount INT       NOT NULL CONSTRAINT DF_Snap_MatNormal DEFAULT (0),
        MaternityCSectionCount INT     NOT NULL CONSTRAINT DF_Snap_MatCs DEFAULT (0),
        MaternitySharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_MatShare DEFAULT (0),
        LargeClaimCostSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_LargeClaim DEFAULT (0),
        Top5ClaimSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Top5 DEFAULT (0),
        SingleLargestClaimSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_SingleLargest DEFAULT (0),
        ClaimVolatilityCv DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Volatility DEFAULT (0),
        NonNetworkCostSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_NonNetwork DEFAULT (0),
        LargestDiseaseCostSharePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_TopDisease DEFAULT (0),
        DataQualityScore DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Snap_DQ DEFAULT (0),
        MappingConfidenceAvg DECIMAL(5,4) NOT NULL CONSTRAINT DF_Snap_MapConf DEFAULT (0),
        ClaimMemberLinkagePct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Snap_Linkage DEFAULT (0),
        TrendNarrative   NVARCHAR(MAX) NULL,
        MetricsJson      NVARCHAR(MAX) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Snap_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Snap_Case ON uw.AnalyticsSnapshot(CaseId, IsCurrent);
END
GO

IF OBJECT_ID(N'uw.AnalyticsBreakdown', N'U') IS NULL
BEGIN
    CREATE TABLE uw.AnalyticsBreakdown(
        AnalyticsBreakdownId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_AnalyticsBreakdown PRIMARY KEY,
        AnalyticsSnapshotId BIGINT    NOT NULL,
        Dimension        NVARCHAR(30)  NOT NULL,   -- AgeBand, Relationship, Gender, DiseaseGroup, ...
        DimensionValue   NVARCHAR(100) NOT NULL,
        Lives            INT           NOT NULL CONSTRAINT DF_Brk_Lives DEFAULT (0),
        ClaimCount       INT           NOT NULL CONSTRAINT DF_Brk_Claims DEFAULT (0),
        PaidAmount       DECIMAL(18,2) NOT NULL CONSTRAINT DF_Brk_Paid DEFAULT (0),
        IncurredAmount   DECIMAL(18,2) NOT NULL CONSTRAINT DF_Brk_Incurred DEFAULT (0),
        FrequencyPer100  DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Brk_Freq DEFAULT (0),
        CostSharePct     DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Brk_Share DEFAULT (0),
        AverageClaimAmount DECIMAL(18,2) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Brk_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Brk_Snapshot ON uw.AnalyticsBreakdown(AnalyticsSnapshotId, Dimension);
END
GO

IF OBJECT_ID(N'uw.PricingMasterVersion', N'U') IS NULL
BEGIN
    CREATE TABLE uw.PricingMasterVersion(
        PricingMasterVersionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_PricingMasterVersion PRIMARY KEY,
        VersionLabel     NVARCHAR(30)  NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_PMV_Status DEFAULT (N'Draft') CONSTRAINT CK_PMV_Status CHECK (Status IN (N'Draft',N'Active',N'Retired',N'Superseded')),
        EffectiveFrom    DATE          NOT NULL,
        EffectiveTo      DATE          NULL,
        PublishedByUserId INT          NULL,
        ApprovedByUserId INT           NULL,
        PublishedAtUtc   DATETIME2(3)  NULL,
        Notes            NVARCHAR(1000) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_PMV_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_PMV_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        RowVersion       ROWVERSION
    );
    CREATE UNIQUE INDEX UX_PMV_Label ON uw.PricingMasterVersion(VersionLabel);
END
GO

IF OBJECT_ID(N'uw.PricingBand', N'U') IS NULL
BEGIN
    CREATE TABLE uw.PricingBand(
        PricingBandId    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_PricingBand PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        BandType         NVARCHAR(40)  NOT NULL,   -- MedicalTrend, Ibnr, Credibility, BenchmarkBurn, RiskMargin, BaseExpense, LargeClaimNormalization, StarRejection, Commission, Gst, MaxDiscount
        BandKey          NVARCHAR(80)  NOT NULL,   -- selector key, e.g. 'Age<=45', 'Claims>=300', 'Default', 'Max'
        BandValueJson    NVARCHAR(MAX) NOT NULL,   -- {value, unit, min, max, secondaryMin, secondaryMax, displayOrder}
        DisplayOrder     INT           NOT NULL CONSTRAINT DF_Band_DisplayOrder DEFAULT (0),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Band_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Band_Master ON uw.PricingBand(PricingMasterVersionId, BandType, DisplayOrder);
END
GO

IF OBJECT_ID(N'uw.CommissionMaster', N'U') IS NULL
BEGIN
    CREATE TABLE uw.CommissionMaster(
        CommissionMasterId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_CommissionMaster PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        ChannelType      NVARCHAR(40)  NOT NULL CONSTRAINT DF_Comm_Channel DEFAULT (N'Default'),
        DefaultRatePct   DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Comm_Default DEFAULT (7.5),
        MinRatePct       DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Comm_Min DEFAULT (0),
        MaxRatePct       DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Comm_Max DEFAULT (15),
        IsActive         BIT           NOT NULL CONSTRAINT DF_Comm_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Comm_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Comm_Master ON uw.CommissionMaster(PricingMasterVersionId);
END
GO

IF OBJECT_ID(N'uw.BenefitCostingMaster', N'U') IS NULL
BEGIN
    CREATE TABLE uw.BenefitCostingMaster(
        BenefitCostingMasterId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_BenefitCostingMaster PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        BenefitCode      NVARCHAR(40)  NOT NULL,
        UtilizationRate  DECIMAL(9,6)  NULL,
        SeverityCaptureRate DECIMAL(9,6) NULL,
        IncidenceRate    DECIMAL(9,6)  NULL,
        SeverityAmount   DECIMAL(18,2) NULL,
        FactorPct        DECIMAL(9,6)  NULL,
        ExperienceCredibilityPct DECIMAL(9,4) NULL,
        AttributionFactor DECIMAL(9,6) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Bcm_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Bcm_Master ON uw.BenefitCostingMaster(PricingMasterVersionId, BenefitCode);
END
GO

IF OBJECT_ID(N'uw.RoomRentFactor', N'U') IS NULL
BEGIN
    CREATE TABLE uw.RoomRentFactor(
        RoomRentFactorId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_RoomRentFactor PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        RoomCategory     NVARCHAR(20)  NOT NULL,  -- OnePercent, TwoPercent, SingleRoom, Actual
        CostFactor       DECIMAL(9,6)  NOT NULL,
        DisplayOrder     INT           NOT NULL CONSTRAINT DF_Rrf_DisplayOrder DEFAULT (0),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Rrf_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Rrf_Master ON uw.RoomRentFactor(PricingMasterVersionId);
END
GO

IF OBJECT_ID(N'uw.StandardLoadingMaster', N'U') IS NULL
BEGIN
    CREATE TABLE uw.StandardLoadingMaster(
        StandardLoadingMasterId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_StandardLoadingMaster PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        LoadingName      NVARCHAR(200) NOT NULL,
        DefaultLoadingPct DECIMAL(9,4) NOT NULL CONSTRAINT DF_Slm_Default DEFAULT (0),
        AppliesToBurnPremium BIT       NOT NULL CONSTRAINT DF_Slm_Burn DEFAULT (0),
        AppliesToEnrollmentPremium BIT   NOT NULL CONSTRAINT DF_Slm_Enroll DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_Slm_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Slm_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Slm_Master ON uw.StandardLoadingMaster(PricingMasterVersionId);
END
GO

IF OBJECT_ID(N'uw.LimitChangeFactor', N'U') IS NULL
BEGIN
    CREATE TABLE uw.LimitChangeFactor(
        LimitChangeFactorId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_LimitChangeFactor PRIMARY KEY,
        PricingMasterVersionId INT     NOT NULL,
        LimitType        NVARCHAR(40)  NOT NULL,  -- SumInsured, RoomRent, MaternityCap, SubLimit
        ExpiringMin      DECIMAL(18,2) NULL,
        ExpiringMax      DECIMAL(18,2) NULL,
        ProposedMin      DECIMAL(18,2) NULL,
        ProposedMax      DECIMAL(18,2) NULL,
        FactorPct        DECIMAL(9,4)  NOT NULL CONSTRAINT DF_Lcf_Factor DEFAULT (0),
        Direction        NVARCHAR(10)  NOT NULL CONSTRAINT DF_Lcf_Direction DEFAULT (N'Loading') CONSTRAINT CK_Lcf_Direction CHECK (Direction IN (N'Loading',N'Discount')),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Lcf_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Lcf_Master ON uw.LimitChangeFactor(PricingMasterVersionId);
END
GO

IF OBJECT_ID(N'uw.PricingRun', N'U') IS NULL
BEGIN
    CREATE TABLE uw.PricingRun(
        PricingRunId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_PricingRun PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        AnalyticsSnapshotId BIGINT     NULL,
        PricingMasterVersionId INT     NULL,
        CaseStageRunId   BIGINT        NULL,
        HasOverrides     BIT           NOT NULL CONSTRAINT DF_PRun_HasOverrides DEFAULT (0),
        ObservedBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Observed DEFAULT (0),
        LargeClaimNormalizationPct DECIMAL(9,4) NOT NULL CONSTRAINT DF_PRun_Lcn DEFAULT (0),
        StarRejectionPct DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_Star DEFAULT (0),
        IbnrPct          DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_Ibnr DEFAULT (0),
        UltimateBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Ultimate DEFAULT (0),
        CredibilityZPct  DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_CredZ DEFAULT (0),
        BenchmarkBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Benchmark DEFAULT (0),
        CredibleBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Credible DEFAULT (0),
        MedicalTrendPct  DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_Trend DEFAULT (0),
        ProjectedBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Projected DEFAULT (0),
        BenefitDeltaPerLife DECIMAL(18,2) NULL,
        FinalProjectedBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_FinalProjected DEFAULT (0),
        NetTechnicalPremium DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_NetTechnical DEFAULT (0),
        BaseExpensePct   DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_BaseExpense DEFAULT (0),
        RiskMarginPct    DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_RiskMargin DEFAULT (0),
        CommissionPct    DECIMAL(9,4)  NOT NULL CONSTRAINT DF_PRun_Commission DEFAULT (0),
        GrossedUpPremium DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_GrossedUp DEFAULT (0),
        MaternityLoadingAmount DECIMAL(18,2) NULL,
        StandardLoadingAmount DECIMAL(18,2) NULL,
        LimitChangeAmount DECIMAL(18,2) NULL,
        ManualAdjustmentPct DECIMAL(9,4) NULL,
        TechnicalPremium DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Technical DEFAULT (0),
        GrossPremium     DECIMAL(18,2) NOT NULL CONSTRAINT DF_PRun_Gross DEFAULT (0),
        InputsHash       CHAR(64)      NULL,
        InputSnapshotJson NVARCHAR(MAX) NULL,
        ComponentsJson   NVARCHAR(MAX) NULL,     -- uw.PricingComponent rows serialized by the app
        ComputedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_PRun_ComputedAtUtc DEFAULT (SYSUTCDATETIME()),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_PRun_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_PRun_Case ON uw.PricingRun(CaseId);
END
GO

IF OBJECT_ID(N'uw.PricingComponent', N'U') IS NULL
BEGIN
    CREATE TABLE uw.PricingComponent(
        PricingComponentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_PricingComponent PRIMARY KEY,
        PricingRunId     BIGINT        NOT NULL,
        StepOrder        INT           NOT NULL,
        StepCode         NVARCHAR(80)  NOT NULL,
        StepLabel        NVARCHAR(200) NOT NULL CONSTRAINT DF_PComp_Label DEFAULT (N''),
        SourceType       NVARCHAR(20)  NOT NULL CONSTRAINT DF_PComp_Source DEFAULT (N'Computed'), -- Master|Override|Computed|BenefitDelta|Discount|Tax
        SourceRef        NVARCHAR(200) NULL,       -- master band id or override id
        InputValue       DECIMAL(18,6) NULL,
        FactorApplied    DECIMAL(18,6) NULL,
        Value            DECIMAL(18,6) NOT NULL CONSTRAINT DF_PComp_Value DEFAULT (0),
        Unit             NVARCHAR(20)  NOT NULL CONSTRAINT DF_PComp_Unit DEFAULT (N''),
        Basis            NVARCHAR(200) NULL,
        IsOverride       BIT           NOT NULL CONSTRAINT DF_PComp_IsOverride DEFAULT (0),
        OverrideReason   NVARCHAR(1000) NULL,
        Explanation      NVARCHAR(1000) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_PComp_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_PComp_Run ON uw.PricingComponent(PricingRunId, StepOrder);
END
GO

IF OBJECT_ID(N'uw.PricingOverride', N'U') IS NULL
BEGIN
    CREATE TABLE uw.PricingOverride(
        PricingOverrideId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_PricingOverride PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        QuoteVersionId   BIGINT        NULL,
        OverrideType     NVARCHAR(40)  NOT NULL,
        MasterValue      DECIMAL(18,6) NOT NULL CONSTRAINT DF_POvr_Master DEFAULT (0),
        OverrideValue    DECIMAL(18,6) NOT NULL CONSTRAINT DF_POvr_Override DEFAULT (0),
        Reason           NVARCHAR(1000) NOT NULL,
        CreatedBy        NVARCHAR(100) NOT NULL CONSTRAINT DF_POvr_CreatedBy DEFAULT (N'System'),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_POvr_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        IsActive         BIT           NOT NULL CONSTRAINT DF_POvr_IsActive DEFAULT (1)
    );
    CREATE INDEX IX_POvr_Case ON uw.PricingOverride(CaseId, IsActive);
END
GO
PRINT '04_uw_analytics_pricing done';
GO
