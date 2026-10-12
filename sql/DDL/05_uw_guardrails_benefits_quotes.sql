SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 05_uw_guardrails_benefits_quotes.sql — guardrails, benefit
   schedules/deltas, quote versions, artefacts, underwriting actions
   (db.md §10, §11, §12.1–12.3). Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'uw.GuardrailRule', N'U') IS NULL
BEGIN
    CREATE TABLE uw.GuardrailRule(
        GuardrailRuleId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_GuardrailRule PRIMARY KEY,
        RuleCode         NVARCHAR(40)  NOT NULL,
        RuleName         NVARCHAR(200) NOT NULL,
        Category         NVARCHAR(40)  NOT NULL,   -- DataSufficiency, Volatility, Demographic, Clinical, BenefitCover, Commercial, ReferenceList
        MetricSelector   NVARCHAR(80)  NOT NULL,
        Operator         NVARCHAR(10)  NOT NULL CONSTRAINT DF_Grd_Operator DEFAULT (N'>='),
        Threshold        DECIMAL(18,4) NOT NULL CONSTRAINT DF_Grd_Threshold DEFAULT (0),
        ThresholdSecondary DECIMAL(18,4) NULL,
        Severity         NVARCHAR(10)  NOT NULL CONSTRAINT DF_Grd_Severity DEFAULT (N'Refer') CONSTRAINT CK_Grd_Severity CHECK (Severity IN (N'Refer',N'HardStop',N'Blocking',N'Advisory')),
        MessageTemplate  NVARCHAR(500) NOT NULL CONSTRAINT DF_Grd_Message DEFAULT (N''),
        AppliesToBusinessTypes NVARCHAR(100) NULL,
        AppliesToExperienceBusiness BIT NOT NULL CONSTRAINT DF_Grd_AppliesExp DEFAULT (1),
        PctOfMetric      NVARCHAR(80)  NULL,
        IsEnabled        BIT           NOT NULL CONSTRAINT DF_Grd_IsEnabled DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Grd_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Grd_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Grd_RuleCode ON uw.GuardrailRule(RuleCode);
END
GO

IF OBJECT_ID(N'uw.GuardrailThreshold', N'U') IS NULL
BEGIN
    CREATE TABLE uw.GuardrailThreshold(
        GuardrailThresholdId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_GuardrailThreshold PRIMARY KEY,
        GuardrailRuleId  INT           NOT NULL,
        PricingMasterVersionId INT     NULL,
        ThresholdValue   DECIMAL(18,4) NOT NULL CONSTRAINT DF_GrdThr_Value DEFAULT (0),
        ThresholdValueSecondary DECIMAL(18,4) NULL,
        ReferenceListCode NVARCHAR(80) NULL,
        EffectiveFrom    DATE          NOT NULL CONSTRAINT DF_GrdThr_From DEFAULT ('1900-01-01'),
        EffectiveTo      DATE          NULL
    );
    CREATE INDEX IX_GrdThr_Rule ON uw.GuardrailThreshold(GuardrailRuleId);
END
GO

IF OBJECT_ID(N'uw.GuardrailEvaluation', N'U') IS NULL
BEGIN
    CREATE TABLE uw.GuardrailEvaluation(
        GuardrailEvaluationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_GuardrailEvaluation PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        CaseStageRunId   BIGINT        NULL,
        AnalyticsSnapshotId BIGINT     NULL,
        PricingRunId     BIGINT        NULL,
        QuoteVersionId   BIGINT        NULL,
        RulesEvaluated   INT           NOT NULL CONSTRAINT DF_GrdEval_Evaluated DEFAULT (0),
        RulesPassed      INT           NOT NULL CONSTRAINT DF_GrdEval_Passed DEFAULT (0),
        RulesReferred    INT           NOT NULL CONSTRAINT DF_GrdEval_Referred DEFAULT (0),
        RulesNotApplicable INT         NOT NULL CONSTRAINT DF_GrdEval_NA DEFAULT (0),
        QuoteConfidence  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_GrdEval_Conf DEFAULT (0),
        IsAutoQuoteEligible BIT        NOT NULL CONSTRAINT DF_GrdEval_AutoQuote DEFAULT (0),
        ResultJson       NVARCHAR(MAX) NULL,     -- full GuardrailResult serialized
        EvaluatedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_GrdEval_AtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_GrdEval_Case ON uw.GuardrailEvaluation(CaseId);
END
GO

IF OBJECT_ID(N'uw.GuardrailResult', N'U') IS NULL
BEGIN
    CREATE TABLE uw.GuardrailResult(
        GuardrailResultId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_GuardrailResult PRIMARY KEY,
        GuardrailEvaluationId BIGINT  NOT NULL,
        GuardrailRuleId  INT           NULL,
        RuleCode         NVARCHAR(40)  NOT NULL CONSTRAINT DF_GrdRes_Code DEFAULT (N''),
        ObservedValue    DECIMAL(18,4) NOT NULL CONSTRAINT DF_GrdRes_Observed DEFAULT (0),
        ThresholdValue   DECIMAL(18,4) NOT NULL CONSTRAINT DF_GrdRes_Threshold DEFAULT (0),
        Outcome          NVARCHAR(10)  NOT NULL CONSTRAINT DF_GrdRes_Outcome DEFAULT (N'Pass') CONSTRAINT CK_GrdRes_Outcome CHECK (Outcome IN (N'Pass',N'Refer',N'NotApplicable')),
        Message          NVARCHAR(500) NULL,
        Severity         NVARCHAR(10)  NULL
    );
    CREATE INDEX IX_GrdRes_Eval ON uw.GuardrailResult(GuardrailEvaluationId, Outcome);
END
GO

IF OBJECT_ID(N'uw.BenefitSchedule', N'U') IS NULL
BEGIN
    CREATE TABLE uw.BenefitSchedule(
        BenefitScheduleId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_BenefitSchedule PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        ScheduleType     NVARCHAR(20)  NOT NULL,  -- Expiring, Proposed, Competitor
        CaseDocumentId   BIGINT        NULL,
        ScheduleVersion  INT           NOT NULL CONSTRAINT DF_BenSch_Version DEFAULT (1),
        IsCurrent        BIT           NOT NULL CONSTRAINT DF_BenSch_IsCurrent DEFAULT (1),
        IsLocked         BIT           NOT NULL CONSTRAINT DF_BenSch_IsLocked DEFAULT (0),
        CoveredLives     INT           NULL,
        BaseSumInsured   DECIMAL(18,2) NULL,
        PedDayOne        BIT           NULL,
        MaternityCovered BIT           NULL,
        MaternityNormalLimit DECIMAL(18,2) NULL,
        MaternityCSectionLimit DECIMAL(18,2) NULL,
        AmbulancePerEvent DECIMAL(18,2) NULL,
        NewbornDayOne    BIT           NULL,
        CorporateBuffer  DECIMAL(18,2) NULL,
        RoomRentCategory NVARCHAR(20)  NULL,
        GeneralCopayPct  DECIMAL(9,4)  NULL,
        ExtractionConfidence DECIMAL(5,4) NULL,
        ExtractionMethod NVARCHAR(30)  NULL,
        LinesJson        NVARCHAR(MAX) NULL,     -- uw.BenefitScheduleLine rows serialized
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_BenSch_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_BenSch_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_BenSch_Case ON uw.BenefitSchedule(CaseId, ScheduleType);
END
GO

IF OBJECT_ID(N'uw.BenefitScheduleLine', N'U') IS NULL
BEGIN
    CREATE TABLE uw.BenefitScheduleLine(
        BenefitScheduleLineId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_BenefitScheduleLine PRIMARY KEY,
        BenefitScheduleId BIGINT       NOT NULL,
        BenefitCode      NVARCHAR(40)  NOT NULL,
        BenefitLabel     NVARCHAR(200) NULL,
        ValueNumeric     DECIMAL(18,2) NULL,
        ValueText        NVARCHAR(500) NULL,
        ValueBoolean     BIT           NULL,
        Unit             NVARCHAR(20)  NULL,
        IsSubLimit       BIT           NOT NULL CONSTRAINT DF_BenLine_IsSubLimit DEFAULT (0),
        IsNonStandard    BIT           NOT NULL CONSTRAINT DF_BenLine_IsNonStandard DEFAULT (0),
        Confidence       DECIMAL(5,4)  NOT NULL CONSTRAINT DF_BenLine_Conf DEFAULT (1),
        Provenance       NVARCHAR(20)  NULL,
        SourceCitation   NVARCHAR(500) NULL,
        IsHumanConfirmed BIT           NOT NULL CONSTRAINT DF_BenLine_Human DEFAULT (0),
        ConfirmedByUserId INT          NULL
    );
    CREATE INDEX IX_BenLine_Schedule ON uw.BenefitScheduleLine(BenefitScheduleId);
END
GO

IF OBJECT_ID(N'uw.BenefitDelta', N'U') IS NULL
BEGIN
    CREATE TABLE uw.BenefitDelta(
        BenefitDeltaId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_BenefitDelta PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        PricingRunId     BIGINT        NULL,
        ExpiringScheduleId BIGINT      NULL,
        ProposedScheduleId BIGINT      NULL,
        BaseBurnPerLife  DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDelta_Base DEFAULT (0),
        TotalDeltaAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDelta_Total DEFAULT (0),
        DeltaPerLife     DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDelta_PerLife DEFAULT (0),
        FinalProjectedBurnPerLife DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDelta_Final DEFAULT (0),
        LinesJson        NVARCHAR(MAX) NULL,     -- BenefitDeltaLine rows serialized
        ComputedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_BenDelta_AtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_BenDelta_Case ON uw.BenefitDelta(CaseId);
END
GO

IF OBJECT_ID(N'uw.BenefitDeltaLine', N'U') IS NULL
BEGIN
    CREATE TABLE uw.BenefitDeltaLine(
        BenefitDeltaLineId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_BenefitDeltaLine PRIMARY KEY,
        BenefitDeltaId   BIGINT        NOT NULL,
        BenefitCode      NVARCHAR(40)  NOT NULL,
        BenefitName      NVARCHAR(200) NULL,
        ExpiringValue    DECIMAL(18,2) NULL,
        ProposedValue    DECIMAL(18,2) NULL,
        Method           NVARCHAR(20)  NOT NULL CONSTRAINT DF_BenDL_Method DEFAULT (N'Master'),
        ExperienceAmount DECIMAL(18,2) NULL,
        MasterExpectedAmount DECIMAL(18,2) NULL,
        ExperienceCredibilityPct DECIMAL(9,4) NOT NULL CONSTRAINT DF_BenDL_Cred DEFAULT (0),
        DeltaAmount      DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDL_DeltaAmount DEFAULT (0),
        DeltaPerLife     DECIMAL(18,2) NOT NULL CONSTRAINT DF_BenDL_DeltaPerLife DEFAULT (0),
        Explanation      NVARCHAR(1000) NULL
    );
    CREATE INDEX IX_BenDL_Delta ON uw.BenefitDeltaLine(BenefitDeltaId);
END
GO

IF OBJECT_ID(N'uw.CompetitorQuote', N'U') IS NULL
BEGIN
    CREATE TABLE uw.CompetitorQuote(
        CompetitorQuoteId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_CompetitorQuote PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        CaseDocumentId   BIGINT        NULL,
        BenefitScheduleId BIGINT       NULL,
        CompetitorName   NVARCHAR(200) NOT NULL CONSTRAINT DF_CQ_Name DEFAULT (N''),
        PremiumBeforeGst DECIMAL(18,2) NULL,
        PremiumInclGst   DECIMAL(18,2) NULL,
        CoveredLives     INT           NULL,
        RatePerLife      DECIMAL(18,2) NULL,
        ExtractionConfidence DECIMAL(5,4) NULL,
        ExtractedAtUtc   DATETIME2(3)  NULL,
        LinesJson        NVARCHAR(MAX) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_CQ_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_CQ_Case ON uw.CompetitorQuote(CaseId);
END
GO

IF OBJECT_ID(N'uw.QuoteVersion', N'U') IS NULL
BEGIN
    CREATE TABLE uw.QuoteVersion(
        QuoteVersionId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_QuoteVersion PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        VersionNo        INT           NOT NULL CONSTRAINT DF_QV_VersionNo DEFAULT (1),
        QuoteNo          NVARCHAR(40)  NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_QV_Status DEFAULT (N'Draft') CONSTRAINT CK_QV_Status CHECK (Status IN (N'Draft',N'Current',N'Superseded',N'Withdrawn')),
        GenerationMode   NVARCHAR(20)  NOT NULL CONSTRAINT DF_QV_GenMode DEFAULT (N'Automatic'),
        RevisionReason   NVARCHAR(40)  NULL,
        RevisionNotes    NVARCHAR(2000) NULL,
        SupersedesQuoteVersionId BIGINT NULL,
        SupersededByQuoteVersionId BIGINT NULL,
        AnalyticsSnapshotId BIGINT     NULL,
        PricingRunId     BIGINT        NULL,
        PricingMasterVersionId INT     NULL,
        GuardrailEvaluationId BIGINT   NULL,
        BenefitDeltaId   BIGINT        NULL,
        CompetitorQuoteId BIGINT       NULL,
        CoveredLives     INT           NOT NULL CONSTRAINT DF_QV_Lives DEFAULT (0),
        TechnicalPremium DECIMAL(18,2) NOT NULL CONSTRAINT DF_QV_Technical DEFAULT (0),
        CommissionPct    DECIMAL(9,4)  NOT NULL CONSTRAINT DF_QV_Commission DEFAULT (0),
        RequestedDiscountPct DECIMAL(9,4) NULL,
        ApprovedDiscountPct DECIMAL(9,4) NULL,
        PremiumBeforeGst DECIMAL(18,2) NOT NULL CONSTRAINT DF_QV_BeforeGst DEFAULT (0),
        GstRatePct       DECIMAL(9,4)  NOT NULL CONSTRAINT DF_QV_GstRate DEFAULT (18),
        GstAmount        DECIMAL(18,2) NOT NULL CONSTRAINT DF_QV_GstAmount DEFAULT (0),
        TotalPremiumInclGst DECIMAL(18,2) NOT NULL CONSTRAINT DF_QV_Total DEFAULT (0),
        RatePerLifeBeforeGst DECIMAL(18,2) NOT NULL CONSTRAINT DF_QV_RatePerLife DEFAULT (0),
        DeviationFromTechnicalPct DECIMAL(9,4) NOT NULL CONSTRAINT DF_QV_Deviation DEFAULT (0),
        QuoteConfidence  DECIMAL(5,2)  NULL,
        IsAutoQuote      BIT           NOT NULL CONSTRAINT DF_QV_IsAutoQuote DEFAULT (0),
        ValidUntilDate   DATE          NULL,
        PublishedByUserId INT          NULL,
        PublishedAtUtc   DATETIME2(3)  NULL,
        ApprovedByUserId INT           NULL,
        ApprovalAuthorityLevel NVARCHAR(30) NULL,
        SnapshotJson     NVARCHAR(MAX) NOT NULL CONSTRAINT DF_QV_SnapshotJson DEFAULT (N'{}'),
        InputsHash       CHAR(64)      NULL,
        PricingJson      NVARCHAR(MAX) NULL,     -- PricingResult serialized (Core model)
        AnalyticsJson    NVARCHAR(MAX) NULL,     -- AnalyticsSnapshot serialized
        GuardrailsJson   NVARCHAR(MAX) NULL,     -- GuardrailResult serialized
        BenefitDeltaJson NVARCHAR(MAX) NULL,     -- BenefitDeltaLine list serialized
        CreatedBy        NVARCHAR(100) NOT NULL CONSTRAINT DF_QV_CreatedBy DEFAULT (N'Pipeline'),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_QV_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_QV_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_QV_Supersedes FOREIGN KEY (SupersedesQuoteVersionId) REFERENCES uw.QuoteVersion(QuoteVersionId)
    );
    CREATE UNIQUE INDEX UX_QV_Case_Version ON uw.QuoteVersion(CaseId, VersionNo);
    CREATE UNIQUE INDEX UX_QV_QuoteNo ON uw.QuoteVersion(QuoteNo);
    /* Exactly one live quote per case — enforced by the database (db §12.1). */
    CREATE UNIQUE INDEX UX_QV_OneCurrent ON uw.QuoteVersion(CaseId) WHERE Status = N'Current';
END
GO

IF OBJECT_ID(N'uw.QuoteArtifact', N'U') IS NULL
BEGIN
    CREATE TABLE uw.QuoteArtifact(
        QuoteArtifactId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_QuoteArtifact PRIMARY KEY,
        QuoteVersionId   BIGINT        NOT NULL,
        ArtifactType     NVARCHAR(30)  NOT NULL,  -- Pdf, ExcelDetail, ExcelSummary, Json, ManualQuoteUpload
        StorageUri       NVARCHAR(2048) NOT NULL,
        FileName         NVARCHAR(512) NOT NULL,
        SizeBytes        BIGINT        NOT NULL CONSTRAINT DF_QA_Size DEFAULT (0),
        Sha256           CHAR(64)      NOT NULL CONSTRAINT DF_QA_Sha256 DEFAULT (REPLICATE('0',64)),
        GeneratedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_QA_GeneratedAtUtc DEFAULT (SYSUTCDATETIME()),
        GeneratedBy      NVARCHAR(100) NOT NULL CONSTRAINT DF_QA_GeneratedBy DEFAULT (N'System'),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_QA_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_QA_Quote ON uw.QuoteArtifact(QuoteVersionId);
END
GO

IF OBJECT_ID(N'uw.UnderwritingAction', N'U') IS NULL
BEGIN
    /* Append-only decision log (db §12.3). */
    CREATE TABLE uw.UnderwritingAction(
        UnderwritingActionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_UnderwritingAction PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        QuoteVersionId   BIGINT        NULL,
        ActionType       NVARCHAR(40)  NOT NULL,
        Outcome          NVARCHAR(40)  NULL,
        PerformedByUserId INT          NULL,
        Actor            NVARCHAR(100) NOT NULL CONSTRAINT DF_UA_Actor DEFAULT (N'System'),
        AuthorityLevel   NVARCHAR(30)  NULL,
        AuthorityCheck   NVARCHAR(20)  NOT NULL CONSTRAINT DF_UA_AuthorityCheck DEFAULT (N'Pass'),
        Remarks          NVARCHAR(2000) NULL,
        ConditionsJson   NVARCHAR(MAX) NULL,
        PayloadJson      NVARCHAR(MAX) NULL,
        PerformedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_UA_AtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_UA_Case ON uw.UnderwritingAction(CaseId);
END
GO

IF OBJECT_ID(N'uw.DiscountRequest', N'U') IS NULL
BEGIN
    CREATE TABLE uw.DiscountRequest(
        DiscountRequestId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_DiscountRequest PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        QuoteVersionId   BIGINT        NULL,
        RequestedByUserId INT          NULL,
        RequestedPct     DECIMAL(9,4)  NOT NULL CONSTRAINT DF_DR_Pct DEFAULT (0),
        RequestedRemarks NVARCHAR(2000) NULL,
        JustificationType NVARCHAR(40) NULL,
        CompetitorQuoteId BIGINT       NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_DR_Status DEFAULT (N'Pending'),
        ApprovedPct      DECIMAL(9,4)  NULL,
        ApprovedByUserId INT           NULL,
        ApprovalRemarks  NVARCHAR(2000) NULL,
        ResultingQuoteVersionId BIGINT NULL,
        RequestedAtUtc   DATETIME2(3)  NOT NULL CONSTRAINT DF_DR_AtUtc DEFAULT (SYSUTCDATETIME()),
        DecidedAtUtc     DATETIME2(3)  NULL
    );
    CREATE INDEX IX_DR_Case ON uw.DiscountRequest(CaseId);
END
GO

IF OBJECT_ID(N'uw.Referral', N'U') IS NULL
BEGIN
    CREATE TABLE uw.Referral(
        ReferralId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_Referral PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        QuoteVersionId   BIGINT        NULL,
        ReferralSource   NVARCHAR(40)  NOT NULL,
        ReferredByUserId INT           NULL,
        ReferralReason   NVARCHAR(1000) NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_Ref_Status DEFAULT (N'Open'),
        AssignedToUserId INT           NULL,
        QueueCategory    NVARCHAR(40)  NULL,
        SlaDueAtUtc      DATETIME2(3)  NULL,
        ClosedAtUtc      DATETIME2(3)  NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Ref_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Ref_Case ON uw.Referral(CaseId);
    CREATE INDEX IX_Ref_Queue ON uw.Referral(QueueCategory, Status);
END
GO

IF OBJECT_ID(N'uw.AuthorityMatrix', N'U') IS NULL
BEGIN
    CREATE TABLE uw.AuthorityMatrix(
        AuthorityMatrixId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_uw_AuthorityMatrix PRIMARY KEY,
        PricingMasterVersionId INT     NULL,
        AuthorityLevel   NVARCHAR(30)  NOT NULL,  -- Underwriter, SeniorUnderwriter, ChiefUnderwriter
        MaxPremiumAmount DECIMAL(18,2) NULL,       -- null = unlimited
        MaxDeviationPct  DECIMAL(9,4)  NOT NULL CONSTRAINT DF_AM_Deviation DEFAULT (0),
        MaxDiscountPct   DECIMAL(9,4)  NOT NULL CONSTRAINT DF_AM_Discount DEFAULT (0),
        CanDecline       BIT           NOT NULL CONSTRAINT DF_AM_CanDecline DEFAULT (0),
        CanWaiveException BIT          NOT NULL CONSTRAINT DF_AM_CanWaive DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_AM_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_AM_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_AM_Level ON uw.AuthorityMatrix(AuthorityLevel);
END
GO
PRINT '05_uw_guardrails_benefits_quotes done';
GO
