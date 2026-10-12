SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 06_kb.sql — knowledge base: master columns, synonyms,
   taxonomy, ICD reference, golden dataset, vectors, term proposals
   (db.md §7.1–7.9). Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'kb.TaxonomyVersion', N'U') IS NULL
BEGIN
    CREATE TABLE kb.TaxonomyVersion(
        TaxonomyVersionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_TaxonomyVersion PRIMARY KEY,
        VersionLabel     NVARCHAR(30)  NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_TaxV_Status DEFAULT (N'Draft') CONSTRAINT CK_TaxV_Status CHECK (Status IN (N'Draft',N'Active',N'Retired')),
        EffectiveFrom    DATE          NULL,
        EffectiveTo      DATE          NULL,
        EvaluationAccuracy DECIMAL(5,4) NULL,
        EvaluationReviewRate DECIMAL(5,4) NULL,
        EvaluationUnresolvedRate DECIMAL(5,4) NULL,
        PromotedBy       NVARCHAR(100) NULL,
        PromotedAtUtc    DATETIME2(3)  NULL,
        Notes            NVARCHAR(1000) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_TaxV_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_TaxV_Label ON kb.TaxonomyVersion(VersionLabel);
END
GO

IF OBJECT_ID(N'kb.IcdReference', N'U') IS NULL
BEGIN
    CREATE TABLE kb.IcdReference(
        IcdCode          VARCHAR(20)   NOT NULL CONSTRAINT PK_kb_IcdReference PRIMARY KEY,
        Description      NVARCHAR(500) NOT NULL,
        ChapterCode      NVARCHAR(10)  NULL,
        ChapterName      NVARCHAR(200) NULL,
        BlockCode        NVARCHAR(10)  NULL,
        IsBillable       BIT           NOT NULL CONSTRAINT DF_Icd_IsBillable DEFAULT (1),
        IsActive         BIT           NOT NULL CONSTRAINT DF_Icd_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Icd_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF OBJECT_ID(N'kb.MasterColumn', N'U') IS NULL
BEGIN
    CREATE TABLE kb.MasterColumn(
        MasterColumnId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_MasterColumn PRIMARY KEY,
        FileCategory     NVARCHAR(20)  NOT NULL,   -- Claim, Enrollment, Census
        ParameterName    NVARCHAR(200) NOT NULL,
        DisplayName      NVARCHAR(200) NOT NULL,
        TargetEntity     NVARCHAR(30)  NOT NULL,
        TargetField      NVARCHAR(100) NOT NULL,
        DataType         NVARCHAR(30)  NOT NULL CONSTRAINT DF_MC_DataType DEFAULT (N'Text'),
        IsMandatory      BIT           NOT NULL CONSTRAINT DF_MC_IsMandatory DEFAULT (0),
        DisplayOrder     INT           NOT NULL CONSTRAINT DF_MC_DisplayOrder DEFAULT (0),
        Description      NVARCHAR(500) NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_MC_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_MC_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_MC_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_MC_Category_Parameter ON kb.MasterColumn(FileCategory, ParameterName) WHERE IsActive = 1;
END
GO

IF OBJECT_ID(N'kb.MasterColumnSynonym', N'U') IS NULL
BEGIN
    CREATE TABLE kb.MasterColumnSynonym(
        SynonymId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_MasterColumnSynonym PRIMARY KEY,
        MasterColumnId   INT           NULL,
        TargetParameter  NVARCHAR(200) NULL,
        SynonymText      NVARCHAR(400) NOT NULL,
        NormalizedSynonym NVARCHAR(400) NOT NULL,
        InsurerId        INT           NULL,
        TpaId            INT           NULL,
        Origin           NVARCHAR(20)  NOT NULL CONSTRAINT DF_Syn_Origin DEFAULT (N'Seed') CONSTRAINT CK_Syn_Origin CHECK (Origin IN (N'Seed',N'LearnedFromApproval',N'Admin',N'Learned')),
        UseCount         INT           NOT NULL CONSTRAINT DF_Syn_UseCount DEFAULT (0),
        LearnedFromApproval BIT        NOT NULL CONSTRAINT DF_Syn_Learned DEFAULT (0),
        LearnedFromCase  NVARCHAR(32)  NULL,
        ScopedInsurerTpa NVARCHAR(200) NULL,
        LastUsedAtUtc    DATETIME2(3)  NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Syn_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Syn_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Syn_Match ON kb.MasterColumnSynonym(NormalizedSynonym, MasterColumnId, InsurerId, TpaId) WHERE IsActive = 1;
    CREATE INDEX IX_Syn_Normalized ON kb.MasterColumnSynonym(NormalizedSynonym);
END
GO

IF OBJECT_ID(N'kb.DiseaseTaxonomy', N'U') IS NULL
BEGIN
    CREATE TABLE kb.DiseaseTaxonomy(
        TaxonomyId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_DiseaseTaxonomy PRIMARY KEY,
        StandardDiseaseName NVARCHAR(300) NOT NULL,
        IcdCode          VARCHAR(20)   NOT NULL,
        DiagnosisCategory NVARCHAR(500) NULL,
        DiseaseGroup     NVARCHAR(200) NULL,
        Department       NVARCHAR(100) NULL,
        IsChronic        BIT           NOT NULL CONSTRAINT DF_Tax_IsChronic DEFAULT (0),
        BenefitGroup     NVARCHAR(100) NULL,
        MatchKeywordsJson NVARCHAR(MAX) NULL,
        DefaultLevel     TINYINT       NOT NULL CONSTRAINT DF_Tax_DefaultLevel DEFAULT (1),
        TaxonomyVersionId INT          NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Tax_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Tax_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Tax_Name_Icd ON kb.DiseaseTaxonomy(StandardDiseaseName, IcdCode) WHERE IsActive = 1;
    CREATE INDEX IX_Tax_Icd ON kb.DiseaseTaxonomy(IcdCode);
END
GO

IF OBJECT_ID(N'kb.DiseaseAlias', N'U') IS NULL
BEGIN
    CREATE TABLE kb.DiseaseAlias(
        AliasId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_DiseaseAlias PRIMARY KEY,
        TaxonomyId       INT           NOT NULL,
        AliasText        NVARCHAR(400) NOT NULL,
        NormalizedAlias  NVARCHAR(400) NOT NULL,
        MatchKeywords    NVARCHAR(1000) NULL,
        Origin           NVARCHAR(20)  NOT NULL CONSTRAINT DA_Origin DEFAULT (N'Seed') CONSTRAINT CK_DA_Origin CHECK (Origin IN (N'Seed',N'LearnedFromReview',N'Admin')),
        UseCount         INT           NOT NULL CONSTRAINT DF_DA_UseCount DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_DA_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_DA_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_DA_Tax ON kb.DiseaseAlias(TaxonomyId);
    CREATE INDEX IX_DA_Normalized ON kb.DiseaseAlias(NormalizedAlias);
END
GO

IF OBJECT_ID(N'kb.GoldenVersion', N'U') IS NULL
BEGIN
    CREATE TABLE kb.GoldenVersion(
        GoldenVersionId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_GoldenVersion PRIMARY KEY,
        VersionLabel     NVARCHAR(30)  NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_GV_Status DEFAULT (N'Draft'),
        EntryCount       INT           NOT NULL CONSTRAINT DF_GV_EntryCount DEFAULT (0),
        BuiltAtUtc       DATETIME2(3)  NULL,
        BuiltBy          NVARCHAR(100) NULL,
        EvaluationJson   NVARCHAR(MAX) NULL,
        IsCurrent        BIT           NOT NULL CONSTRAINT DF_GV_IsCurrent DEFAULT (0)
    );
    CREATE UNIQUE INDEX UX_GV_Label ON kb.GoldenVersion(VersionLabel);
END
GO

IF OBJECT_ID(N'kb.GoldenEntry', N'U') IS NULL
BEGIN
    CREATE TABLE kb.GoldenEntry(
        GoldenEntryId    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_GoldenEntry PRIMARY KEY,
        GoldenVersionId  INT           NULL,
        GoldenVersion    NVARCHAR(30)  NOT NULL CONSTRAINT DF_GE_GoldenVersion DEFAULT (N'GD-v1.0'),
        SourceText       NVARCHAR(MAX) NOT NULL,
        NormalizedText   NVARCHAR(900) NOT NULL,   -- RawTextNorm in Core
        DiagnosisCategory NVARCHAR(500) NULL,
        IcdLevel1        VARCHAR(20)   NOT NULL,
        IcdLevel1Name    NVARCHAR(300) NULL,
        IcdLevel2        VARCHAR(20)   NULL,
        IcdLevel2Name    NVARCHAR(300) NULL,
        Level1TaxonomyId INT           NULL,
        Level2TaxonomyId INT           NULL,
        AiSuggestionJson NVARCHAR(MAX) NULL,
        HumanCorrectionJson NVARCHAR(MAX) NULL,
        WasCorrected     BIT           NOT NULL CONSTRAINT DF_GE_WasCorrected DEFAULT (0),
        ApprovedByUserId INT           NULL,
        ApprovedAtUtc    DATETIME2(3)  NULL,
        SourceCodingReviewId BIGINT    NULL,
        Source           NVARCHAR(30)  NOT NULL CONSTRAINT DF_GE_Source DEFAULT (N'Migration') CONSTRAINT CK_GE_Source CHECK (Source IN (N'Migration',N'ReviewCorrection')),
        UseCount         INT           NOT NULL CONSTRAINT DF_GE_UseCount DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_GE_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_GE_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_GE_Normalized ON kb.GoldenEntry(NormalizedText);
    CREATE INDEX IX_GE_Version ON kb.GoldenEntry(GoldenVersionId, IsActive);
END
GO

IF OBJECT_ID(N'kb.RetrievalVector', N'U') IS NULL
BEGIN
    CREATE TABLE kb.RetrievalVector(
        VectorId         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_RetrievalVector PRIMARY KEY,
        EntityType       NVARCHAR(30)  NOT NULL,   -- GoldenEntry, HeaderMapping
        EntityId         BIGINT        NOT NULL,
        GoldenVersionId  INT           NULL,
        EmbeddingModel   NVARCHAR(100) NOT NULL,
        EmbeddingVersion NVARCHAR(30)  NOT NULL,
        Dimensions       SMALLINT      NOT NULL,
        Encoding         NVARCHAR(20)  NOT NULL CONSTRAINT DF_RV_Encoding DEFAULT (N'Float32LE'),
        Vector           VARBINARY(MAX) NOT NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_RV_IsActive DEFAULT (1),
        BuiltAtUtc       DATETIME2(3)  NOT NULL CONSTRAINT DF_RV_BuiltAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_RV_Entity ON kb.RetrievalVector(EntityType, EntityId, EmbeddingVersion);
    CREATE INDEX IX_RV_GoldenVersion ON kb.RetrievalVector(GoldenVersionId, IsActive);
END
GO

IF OBJECT_ID(N'kb.TermProposal', N'U') IS NULL
BEGIN
    CREATE TABLE kb.TermProposal(
        TermProposalId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_kb_TermProposal PRIMARY KEY,
        ProposedByUserId INT           NULL,
        CaseId           BIGINT        NULL,
        SourceText       NVARCHAR(MAX) NOT NULL,
        ProposedDiseaseName NVARCHAR(300) NULL,
        ProposedIcdCode  VARCHAR(20)   NULL,
        ProposedGroup    NVARCHAR(200) NULL,
        Justification    NVARCHAR(1000) NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_TP_Status DEFAULT (N'Pending') CONSTRAINT CK_TP_Status CHECK (Status IN (N'Pending',N'Accepted',N'Rejected')),
        ReviewedByUserId INT           NULL,
        ReviewedAtUtc    DATETIME2(3)  NULL,
        ResultingTaxonomyId INT        NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_TP_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_TP_Status ON kb.TermProposal(Status);
END
GO
PRINT '06_kb done';
GO
