SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 02_core.sql — case spine (db.md §4.1–4.5) + case number seq.
   Conventions: BIGINT IDENTITY PKs, DECIMAL(18,2) money, DECIMAL(5,4)
   confidence, DATETIME2(3) UTC, audit columns, RowVersion on human-edited
   tables. Idempotent via IF OBJECT_ID guards.
   ===================================================================== */

IF OBJECT_ID(N'core.[Case]', N'U') IS NULL
BEGIN
    CREATE TABLE core.[Case](
        CaseId                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_core_Case PRIMARY KEY,
        CaseNo                 NVARCHAR(32)  NOT NULL,
        CorporateName          NVARCHAR(255) NOT NULL,
        BusinessType           NVARCHAR(30)  NOT NULL CONSTRAINT CK_Case_BusinessType CHECK (BusinessType IN (N'Rollover',N'Fresh',N'RenewalInternal',N'RenewalExternal')),
        BusinessSubType        NVARCHAR(20)  NULL CONSTRAINT CK_Case_BusinessSubType CHECK (BusinessSubType IN (N'Main',N'Parent',N'Topup')),
        PolicyNo               NVARCHAR(100) NULL,
        PolicyName             NVARCHAR(255) NULL,
        IndustryId             INT           NULL,
        InsurerId              INT           NULL,
        TpaId                  INT           NULL,
        PolicyStartDate        DATE          NULL,
        PolicyEndDate          DATE          NULL,
        ReconDate              DATE          NULL,
        FinancialYear          NVARCHAR(7)   NULL,
        ExpectedLives          INT           NULL,
        Status                 NVARCHAR(30)  NOT NULL CONSTRAINT DF_Case_Status DEFAULT (N'Draft'),
        CurrentStage           NVARCHAR(30)  NULL,
        CurrentStageAttempt    INT           NOT NULL CONSTRAINT DF_Case_CurrentStageAttempt DEFAULT (0),
        OwnerUserId            INT           NULL,
        AssignedUnderwriterId  INT           NULL,
        CurrentQuoteVersionId  BIGINT        NULL,
        QuoteConfidence        DECIMAL(5,2)  NULL,
        IsAutoQuoted           BIT           NOT NULL CONSTRAINT DF_Case_IsAutoQuoted DEFAULT (0),
        SourceSystem           NVARCHAR(30)  NOT NULL CONSTRAINT DF_Case_SourceSystem DEFAULT (N'GMCv2'),
        CreatedBy              NVARCHAR(100) NOT NULL CONSTRAINT DF_Case_CreatedBy DEFAULT (N'System'),
        CreatedAtUtc           DATETIME2(3)  NOT NULL CONSTRAINT DF_Case_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedBy              NVARCHAR(100) NULL,
        UpdatedAtUtc           DATETIME2(3)  NOT NULL CONSTRAINT DF_Case_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        RowVersion             ROWVERSION
    );
    CREATE UNIQUE INDEX UX_Case_CaseNo ON core.[Case](CaseNo);
    CREATE INDEX IX_Case_Status ON core.[Case](Status);
    CREATE INDEX IX_Case_AssignedUnderwriter ON core.[Case](AssignedUnderwriterId);
    CREATE INDEX IX_Case_Owner_Created ON core.[Case](OwnerUserId, CreatedAtUtc);
    CREATE INDEX IX_Case_PolicyNo ON core.[Case](PolicyNo);
    CREATE INDEX IX_Case_CorporateName ON core.[Case](CorporateName);
END
GO

IF OBJECT_ID(N'core.CaseDocument', N'U') IS NULL
BEGIN
    CREATE TABLE core.CaseDocument(
        DocumentId             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_core_CaseDocument PRIMARY KEY,
        CaseId                 BIGINT        NOT NULL CONSTRAINT FK_CaseDocument_Case REFERENCES core.[Case](CaseId),
        DocType                NVARCHAR(30)  NOT NULL,  -- ClaimsRegister, Enrollment, ExpiringPolicy, Rfq, CompetitorQuote, ...
        FileName               NVARCHAR(512) NOT NULL,
        StorageUri             NVARCHAR(2048) NOT NULL,
        ContentType            NVARCHAR(128) NOT NULL CONSTRAINT DF_Doc_ContentType DEFAULT (N'application/octet-stream'),
        SizeBytes              BIGINT        NOT NULL CONSTRAINT DF_Doc_SizeBytes DEFAULT (0),
        Sha256                 CHAR(64)      NOT NULL CONSTRAINT DF_Doc_Sha256 DEFAULT (REPLICATE('0',64)),
        ScanStatus             NVARCHAR(20)  NOT NULL CONSTRAINT DF_Doc_ScanStatus DEFAULT (N'Clean'),
        ParseStatus            NVARCHAR(20)  NOT NULL CONSTRAINT DF_Doc_ParseStatus DEFAULT (N'Pending'), -- Pending|Parsed|Unreadable
        SheetName              NVARCHAR(128) NULL,
        HeaderRowIndex         INT           NULL,
        TotalRows              INT           NULL,
        TotalColumns           INT           NULL,
        ExtractionStatus       NVARCHAR(20)  NULL,
        ExtractionConfidence   DECIMAL(5,4)  NULL,
        SupersededByDocumentId BIGINT        NULL,
        UploadedBy             NVARCHAR(100) NOT NULL CONSTRAINT DF_Doc_UploadedBy DEFAULT (N'System'),
        UploadedAtUtc          DATETIME2(3)  NOT NULL CONSTRAINT DF_Doc_UploadedAtUtc DEFAULT (SYSUTCDATETIME()),
        CreatedAtUtc           DATETIME2(3)  NOT NULL CONSTRAINT DF_Doc_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc           DATETIME2(3)  NOT NULL CONSTRAINT DF_Doc_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_CaseDocument_Case ON core.CaseDocument(CaseId, DocType);
    CREATE INDEX IX_CaseDocument_Sha256 ON core.CaseDocument(Sha256);
END
GO

IF OBJECT_ID(N'core.CaseStageRun', N'U') IS NULL
BEGIN
    CREATE TABLE core.CaseStageRun(
        CaseStageRunId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_core_CaseStageRun PRIMARY KEY,
        CaseId           BIGINT        NOT NULL CONSTRAINT FK_StageRun_Case REFERENCES core.[Case](CaseId),
        Stage            NVARCHAR(30)  NOT NULL,
        AttemptNo        INT           NOT NULL,
        InputsHash       CHAR(64)      NOT NULL,
        State            NVARCHAR(20)  NOT NULL CONSTRAINT DF_StageRun_State DEFAULT (N'Pending'),
        StartedAtUtc     DATETIME2(3)  NULL,
        EndedAtUtc       DATETIME2(3)  NULL,
        DurationMs       DECIMAL(18,2) NULL,
        RowsIn           INT           NULL,
        RowsOut          INT           NULL,
        AiRunId          BIGINT        NULL,
        OutputRef        NVARCHAR(256) NULL,
        DetailJson       NVARCHAR(MAX) NULL,
        ErrorMessage     NVARCHAR(2000) NULL,
        TriggeredBy      NVARCHAR(100) NOT NULL CONSTRAINT DF_StageRun_TriggeredBy DEFAULT (N'System'),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_StageRun_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_StageRun_Case_Stage_Attempt ON core.CaseStageRun(CaseId, Stage, AttemptNo);
    CREATE INDEX IX_StageRun_State_Stage ON core.CaseStageRun(State, Stage);
    CREATE INDEX IX_StageRun_InputsHash ON core.CaseStageRun(InputsHash);
END
GO

IF OBJECT_ID(N'core.CaseException', N'U') IS NULL
BEGIN
    CREATE TABLE core.CaseException(
        CaseExceptionId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_core_CaseException PRIMARY KEY,
        CaseId           BIGINT        NOT NULL CONSTRAINT FK_Exception_Case REFERENCES core.[Case](CaseId),
        CaseStageRunId   BIGINT        NULL,
        ExceptionCode    NVARCHAR(40)  NOT NULL,
        Severity         NVARCHAR(10)  NOT NULL CONSTRAINT CK_Exception_Severity CHECK (Severity IN (N'Blocking',N'Advisory')),
        Stage            NVARCHAR(30)  NOT NULL,
        SubjectType      NVARCHAR(30)  NULL,
        SubjectRef       NVARCHAR(256) NULL,
        AffectedCount    INT           NULL,
        Reason           NVARCHAR(1000) NOT NULL,
        DetailJson       NVARCHAR(MAX) NULL,
        ResolutionTarget NVARCHAR(256) NULL,
        ResumeStage      NVARCHAR(30)  NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_Exception_Status DEFAULT (N'Open'),
        AssignedToUserId INT           NULL,
        ResolvedBy       NVARCHAR(100) NULL,
        ResolvedAtUtc    DATETIME2(3)  NULL,
        ResolutionAction NVARCHAR(1000) NULL,
        WaiverJustification NVARCHAR(1000) NULL,
        SlaDueAtUtc      DATETIME2(3)  NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Exception_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Exception_Case_Status ON core.CaseException(CaseId, Status);
    CREATE INDEX IX_Exception_Status_Severity ON core.CaseException(Status, Severity);
    CREATE INDEX IX_Exception_Code_Status ON core.CaseException(ExceptionCode, Status);
END
GO

IF OBJECT_ID(N'core.CaseNote', N'U') IS NULL
BEGIN
    CREATE TABLE core.CaseNote(
        CaseNoteId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_core_CaseNote PRIMARY KEY,
        CaseId       BIGINT        NOT NULL CONSTRAINT FK_Note_Case REFERENCES core.[Case](CaseId),
        AuthorUserId INT           NULL,
        Author       NVARCHAR(100) NOT NULL CONSTRAINT DF_Note_Author DEFAULT (N'System'),
        Visibility   NVARCHAR(10)  NOT NULL CONSTRAINT DF_Note_Visibility DEFAULT (N'Internal') CONSTRAINT CK_Note_Visibility CHECK (Visibility IN (N'Internal',N'Shared')),
        Body         NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(3)  NOT NULL CONSTRAINT DF_Note_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Note_Case ON core.CaseNote(CaseId);
END
GO

/* Per-FY case number sequence (SqlRepository uses UPDLOCK/HOLDLOCK here). */
IF OBJECT_ID(N'core.CaseNumberSeq', N'U') IS NULL
BEGIN
    CREATE TABLE core.CaseNumberSeq(
        FinancialYear NVARCHAR(7)  NOT NULL CONSTRAINT PK_core_CaseNumberSeq PRIMARY KEY,
        LastSeq       BIGINT       NOT NULL CONSTRAINT DF_Seq_LastSeq DEFAULT (0),
        UpdatedAtUtc  DATETIME2(3) NOT NULL CONSTRAINT DF_Seq_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
END
GO
PRINT '02_core done';
GO
