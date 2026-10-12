SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 03_ing.sql — ingestion, mapping, standardization, coding
   (db.md §5.1–5.6, §6.1–6.2). Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'ing.SourceColumn', N'U') IS NULL
BEGIN
    CREATE TABLE ing.SourceColumn(
        SourceColumnId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_SourceColumn PRIMARY KEY,
        DocumentId       BIGINT        NOT NULL,
        OrdinalPosition  INT           NOT NULL,
        SourceHeader     NVARCHAR(400) NOT NULL,
        NormalizedHeader NVARCHAR(400) NOT NULL,
        DetectedDataType NVARCHAR(30)  NULL CONSTRAINT DF_SrcCol_Type DEFAULT (N'Text'),
        NonEmptyCount    INT           NULL,
        DistinctCount    INT           NULL,
        SampleValuesJson NVARCHAR(1000) NULL,
        TargetParameter  NVARCHAR(200) NULL,
        Confidence       DECIMAL(5,4)  NULL,
        Provenance       NVARCHAR(20)  NULL,
        Reason           NVARCHAR(1000) NULL,
        AlternativesJson NVARCHAR(MAX) NULL,
        IsRequired       BIT           NOT NULL CONSTRAINT DF_SrcCol_IsRequired DEFAULT (0),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_SrcCol_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_SrcCol_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_SourceColumn_Document ON ing.SourceColumn(DocumentId, OrdinalPosition);
    CREATE INDEX IX_SourceColumn_Normalized ON ing.SourceColumn(NormalizedHeader);
END
GO

IF OBJECT_ID(N'ing.ColumnMapping', N'U') IS NULL
BEGIN
    CREATE TABLE ing.ColumnMapping(
        MappingId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_ColumnMapping PRIMARY KEY,
        SourceColumnId   BIGINT        NOT NULL,
        CaseId           BIGINT        NULL,
        DocumentId       BIGINT        NULL,
        MappingVersion   INT           NOT NULL CONSTRAINT DF_Map_Version DEFAULT (1),
        TargetParameter  NVARCHAR(200) NOT NULL,
        MasterColumnId   INT           NULL,
        Confidence       DECIMAL(5,4)  NULL,
        Provenance       NVARCHAR(20)  NOT NULL CONSTRAINT DF_Map_Provenance DEFAULT (N'Dictionary'),
        Reason           NVARCHAR(1000) NULL,
        AlternativesJson NVARCHAR(MAX) NULL,
        IsManualOverride BIT           NOT NULL CONSTRAINT DF_Map_IsManualOverride DEFAULT (0),
        AutoApproved     BIT           NOT NULL CONSTRAINT DF_Map_AutoApproved DEFAULT (0),
        IsApproved       BIT           NOT NULL CONSTRAINT DF_Map_IsApproved DEFAULT (0),
        ApprovedBy       NVARCHAR(100) NULL,
        ApprovedAtUtc    DATETIME2(3)  NULL,
        AiRunId          BIGINT        NULL,
        IsCurrent        BIT           NOT NULL CONSTRAINT DF_Map_IsCurrent DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Map_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Mapping_Source_Version ON ing.ColumnMapping(SourceColumnId, MappingVersion);
    /* One-current mapping per source column (db §5.2). */
    CREATE UNIQUE INDEX UX_Mapping_OneCurrent ON ing.ColumnMapping(SourceColumnId) WHERE IsCurrent = 1;
    CREATE INDEX IX_Mapping_Case ON ing.ColumnMapping(CaseId, IsApproved);
END
GO

IF OBJECT_ID(N'ing.RawRow', N'U') IS NULL
BEGIN
    CREATE TABLE ing.RawRow(
        RawRowId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_RawRow PRIMARY KEY,
        DocumentId   BIGINT        NOT NULL,
        RowNumber    INT           NOT NULL,
        SheetName    NVARCHAR(128) NOT NULL CONSTRAINT DF_RawRow_Sheet DEFAULT (N''),
        PayloadJson  NVARCHAR(MAX) NOT NULL,
        RowHash      CHAR(64)      NOT NULL,
        CreatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_RawRow_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_RawRow_Document_Row ON ing.RawRow(DocumentId, RowNumber);
    CREATE INDEX IX_RawRow_Document_Hash ON ing.RawRow(DocumentId, RowHash);
END
GO

IF OBJECT_ID(N'ing.StdMember', N'U') IS NULL
BEGIN
    CREATE TABLE ing.StdMember(
        StdMemberId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_StdMember PRIMARY KEY,
        CaseId             BIGINT        NOT NULL,
        RawRowId           BIGINT        NULL,
        MemberNumber       NVARCHAR(100) NULL,
        EmployeeRef        NVARCHAR(100) NULL,
        Relationship       NVARCHAR(30)  NOT NULL CONSTRAINT DF_StdMember_Rel DEFAULT (N'Employee'),
        IsDependent        BIT           NOT NULL CONSTRAINT DF_StdMember_IsDep DEFAULT (0),
        IsParent           BIT           NOT NULL CONSTRAINT DF_StdMember_IsPar DEFAULT (0),
        Gender             NVARCHAR(10)  NULL,
        Age                INT           NULL,
        AgeBand            NVARCHAR(10)  NULL,
        DateOfBirth        DATE          NULL,
        City               NVARCHAR(100) NULL,
        State              NVARCHAR(100) NULL,
        GeographyZone      NVARCHAR(30)  NULL,
        SumInsured         DECIMAL(18,2) NULL,
        PolicySubType      NVARCHAR(20)  NULL,
        Department         NVARCHAR(100) NULL,
        ChronicConditions  NVARCHAR(500) NULL,
        HasChronic         BIT           NOT NULL CONSTRAINT DF_StdMember_HasChronic DEFAULT (0),
        EnrollmentStartDate DATE         NULL,
        EnrollmentEndDate  DATE          NULL,
        IsSynthetic        BIT           NOT NULL CONSTRAINT DF_StdMember_IsSynthetic DEFAULT (0),
        ExtraAttributesJson NVARCHAR(MAX) NULL,
        CreatedAtUtc       DATETIME2(3)  NOT NULL CONSTRAINT DF_StdMember_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_StdMember_Case ON ing.StdMember(CaseId);
    CREATE INDEX IX_StdMember_Case_Member ON ing.StdMember(CaseId, MemberNumber);
    CREATE INDEX IX_StdMember_Case_AgeBand ON ing.StdMember(CaseId, AgeBand);
    CREATE INDEX IX_StdMember_Case_Rel ON ing.StdMember(CaseId, Relationship);
END
GO

IF OBJECT_ID(N'ing.StdClaim', N'U') IS NULL
BEGIN
    CREATE TABLE ing.StdClaim(
        StdClaimId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_StdClaim PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        RawRowId         BIGINT        NULL,
        StdMemberId      BIGINT        NULL,
        ClaimNumber      NVARCHAR(100) NULL,
        MemberNumber     NVARCHAR(100) NULL,
        PolicyYear       NVARCHAR(10)  NULL,
        AdmissionDate    DATE          NULL,
        DischargeDate    DATE          NULL,
        IntimationDate   DATE          NULL,
        SettlementDate   DATE          NULL,
        LengthOfStayDays INT           NULL,
        ClaimType        NVARCHAR(30)  NULL,
        RawClaimStatus   NVARCHAR(100) NULL,
        ClaimStatus      NVARCHAR(20)  NOT NULL CONSTRAINT DF_StdClaim_Status DEFAULT (N'Paid'),
        ClaimedAmount    DECIMAL(18,2) NULL CONSTRAINT DF_StdClaim_Claimed DEFAULT (0),
        ApprovedAmount   DECIMAL(18,2) NULL CONSTRAINT DF_StdClaim_Approved DEFAULT (0),
        SanctionedAmount DECIMAL(18,2) NULL CONSTRAINT DF_StdClaim_Sanctioned DEFAULT (0),
        PaidAmount       DECIMAL(18,2) NULL CONSTRAINT DF_StdClaim_Paid DEFAULT (0),
        OutstandingAmount DECIMAL(18,2) NULL CONSTRAINT DF_StdClaim_Outstanding DEFAULT (0),
        IncurredAmount   DECIMAL(18,2) NOT NULL CONSTRAINT DF_StdClaim_Incurred DEFAULT (0),
        IsLargeClaim     BIT           NOT NULL CONSTRAINT DF_StdClaim_IsLarge DEFAULT (0),
        NetworkType      NVARCHAR(20)  NOT NULL CONSTRAINT DF_StdClaim_Network DEFAULT (N'Network'),
        HospitalRef      NVARCHAR(200) NULL,
        City             NVARCHAR(100) NULL,
        State            NVARCHAR(100) NULL,
        DiseaseCategory  NVARCHAR(500) NULL,
        DiagnosisText    NVARCHAR(MAX) NULL,
        ProcedureText    NVARCHAR(MAX) NULL,
        IsDuplicate      BIT           NOT NULL CONSTRAINT DF_StdClaim_IsDup DEFAULT (0),
        ExtraAttributesJson NVARCHAR(MAX) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_StdClaim_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_StdClaim_Case ON ing.StdClaim(CaseId);
    CREATE INDEX IX_StdClaim_Case_Status ON ing.StdClaim(CaseId, ClaimStatus);
    CREATE INDEX IX_StdClaim_Case_Member ON ing.StdClaim(CaseId, StdMemberId);
    CREATE INDEX IX_StdClaim_Case_Large ON ing.StdClaim(CaseId, IsLargeClaim);
END
GO

IF OBJECT_ID(N'ing.DataQualityResult', N'U') IS NULL
BEGIN
    CREATE TABLE ing.DataQualityResult(
        DataQualityResultId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_DataQualityResult PRIMARY KEY,
        CaseId           BIGINT        NOT NULL,
        CaseStageRunId   BIGINT        NULL,
        RuleCode         NVARCHAR(40)  NOT NULL,
        RuleName         NVARCHAR(200) NOT NULL CONSTRAINT DF_DQ_RuleName DEFAULT (N''),
        Outcome          NVARCHAR(10)  NOT NULL CONSTRAINT DF_DQ_Outcome DEFAULT (N'Pass') CONSTRAINT CK_DQ_Outcome CHECK (Outcome IN (N'Pass',N'Warn',N'Fail')),
        ObservedValue    DECIMAL(18,4) NULL,
        ThresholdValue   DECIMAL(18,4) NULL,
        AffectedRows     INT           NULL,
        FailingRowCount  INT           NULL,
        SampleOffendingJson NVARCHAR(MAX) NULL,
        SampleRowIdsJson NVARCHAR(MAX) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_DQ_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_DQ_Case ON ing.DataQualityResult(CaseId, RuleCode);
END
GO

IF OBJECT_ID(N'ing.ClaimCoding', N'U') IS NULL
BEGIN
    CREATE TABLE ing.ClaimCoding(
        ClaimCodingId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_ClaimCoding PRIMARY KEY,
        StdClaimId        BIGINT        NOT NULL,
        CaseId            BIGINT        NOT NULL,
        CodingVersion     INT           NOT NULL CONSTRAINT DF_Coding_Version DEFAULT (1),
        IsCurrent         BIT           NOT NULL CONSTRAINT DF_Coding_IsCurrent DEFAULT (1),
        Level1IcdCode     VARCHAR(20)   NULL,
        Level1DiseaseName NVARCHAR(300) NULL,
        Level1TaxonomyId  INT           NULL,
        Level1Confidence  DECIMAL(5,4)  NULL,
        Level1Provenance  NVARCHAR(20)  NULL,
        Level2IcdCode     VARCHAR(20)   NULL,
        Level2DiseaseName NVARCHAR(300) NULL,
        Level2TaxonomyId  INT           NULL,
        Level2Confidence  DECIMAL(5,4)  NULL,
        Level2Provenance  NVARCHAR(20)  NULL,
        DiseaseGroup      NVARCHAR(200) NULL,
        Department        NVARCHAR(100) NULL,
        IsChronic         BIT           NULL,
        BenefitGroup      NVARCHAR(100) NULL,
        MaternityDeliveryType NVARCHAR(20) NULL,
        ReasonText        NVARCHAR(1000) NULL,
        NeedsHumanReview  BIT           NOT NULL CONSTRAINT DF_Coding_NeedsReview DEFAULT (0),
        CodingStatus      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Coding_Status DEFAULT (N'AutoAccepted'),
        AiRunId           BIGINT        NULL,
        AiBatchId         BIGINT        NULL,
        GoldenEntryId     BIGINT        NULL,
        CreatedAtUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_Coding_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_Coding_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    /* One current coding per standardized claim (db §6.1). */
    CREATE UNIQUE INDEX UX_ClaimCoding_OneCurrent ON ing.ClaimCoding(StdClaimId) WHERE IsCurrent = 1;
    CREATE INDEX IX_ClaimCoding_Case_Status ON ing.ClaimCoding(CaseId, CodingStatus);
    CREATE INDEX IX_ClaimCoding_Case_Benefit ON ing.ClaimCoding(CaseId, BenefitGroup);
    CREATE INDEX IX_ClaimCoding_L1 ON ing.ClaimCoding(Level1IcdCode);
END
GO

IF OBJECT_ID(N'ing.CodingReview', N'U') IS NULL
BEGIN
    CREATE TABLE ing.CodingReview(
        CodingReviewId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ing_CodingReview PRIMARY KEY,
        ClaimCodingId    BIGINT        NOT NULL,
        ReviewerUserId   INT           NULL,
        Reviewer         NVARCHAR(100) NOT NULL CONSTRAINT DF_CodingReview_Reviewer DEFAULT (N'System'),
        Decision         NVARCHAR(20)  NOT NULL,
        SuggestedLevel1Json NVARCHAR(MAX) NULL,
        SuggestedLevel2Json NVARCHAR(MAX) NULL,
        FinalLevel1Json  NVARCHAR(MAX) NULL,
        FinalLevel2Json  NVARCHAR(MAX) NULL,
        Comments         NVARCHAR(1000) NULL,
        PromotedToGolden BIT           NOT NULL CONSTRAINT DF_CodingReview_Promoted DEFAULT (0),
        ReviewedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_CodingReview_ReviewedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_CodingReview_Coding ON ing.CodingReview(ClaimCodingId);
END
GO
PRINT '03_ing done';
GO
