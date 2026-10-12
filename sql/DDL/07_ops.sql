SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 07_ops.sql — jobs, AI telemetry, configuration, audit,
   notifications (db.md §15). Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'ops.AiRun', N'U') IS NULL
BEGIN
    CREATE TABLE ops.AiRun(
        AiRunId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_AiRun PRIMARY KEY,
        CaseId           BIGINT        NULL,
        CaseStageRunId   BIGINT        NULL,
        Purpose          NVARCHAR(30)  NOT NULL,   -- Extraction, Mapping, Clinical, Narrative, Embedding
        Provider         NVARCHAR(100) NOT NULL CONSTRAINT DF_AiRun_Provider DEFAULT (N''),
        Model            NVARCHAR(100) NOT NULL CONSTRAINT DF_AiRun_Model DEFAULT (N''),
        PromptVersion    NVARCHAR(100) NOT NULL CONSTRAINT DF_AiRun_PromptVersion DEFAULT (N''),
        SchemaVersion    NVARCHAR(100) NOT NULL CONSTRAINT DF_AiRun_SchemaVersion DEFAULT (N''),
        BaseUrlHost      NVARCHAR(200) NULL,
        BatchSize        INT           NULL,
        ConcurrencyCap   INT           NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_AiRun_Status DEFAULT (N'Running') CONSTRAINT CK_AiRun_Status CHECK (Status IN (N'Running',N'Succeeded',N'Partial',N'Failed')),
        StartedAtUtc     DATETIME2(3)  NULL,
        EndedAtUtc       DATETIME2(3)  NULL,
        DurationMs       BIGINT        NULL,
        ItemsTotal       INT           NOT NULL CONSTRAINT DF_AiRun_ItemsTotal DEFAULT (0),
        ItemsResolvedDeterministic INT NOT NULL CONSTRAINT DF_AiRun_Det DEFAULT (0),
        ItemsResolvedRetrieval INT     NOT NULL CONSTRAINT DF_AiRun_Ret DEFAULT (0),
        ItemsSentToModel INT           NOT NULL CONSTRAINT DF_AiRun_Sent DEFAULT (0),
        BatchCount       INT           NOT NULL CONSTRAINT DF_AiRun_BatchCount DEFAULT (0),
        ItemsWritten     INT           NOT NULL CONSTRAINT DF_AiRun_Written DEFAULT (0),
        ItemsInReview    INT           NOT NULL CONSTRAINT DF_AiRun_Review DEFAULT (0),
        ItemsFailed      INT           NOT NULL CONSTRAINT DF_AiRun_Failed DEFAULT (0),
        DeterministicDurationMs BIGINT NULL,
        ModelDurationMs  BIGINT        NULL,
        InputTokens      BIGINT        NULL,
        OutputTokens     BIGINT        NULL,
        ThinkingTokens   BIGINT        NULL,
        TotalTokens      BIGINT        NULL,
        EstimatedCostUsd DECIMAL(12,6) NULL,
        MsPerModelItem   DECIMAL(12,2) NULL,
        MsPerDeterministicItem DECIMAL(12,2) NULL,
        ErrorMessage     NVARCHAR(400) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_AiRun_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_AiRun_Case ON ops.AiRun(CaseId);
    CREATE INDEX IX_AiRun_Status ON ops.AiRun(Status);
END
GO

IF OBJECT_ID(N'ops.AiBatch', N'U') IS NULL
BEGIN
    /* One row per HTTP call. No prompts, no clinical text, no keys. */
    CREATE TABLE ops.AiBatch(
        AiBatchId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_AiBatch PRIMARY KEY,
        AiRunId          BIGINT        NOT NULL,
        BatchNumber      INT           NOT NULL,
        ItemCount        INT           NOT NULL CONSTRAINT DF_AiBatch_Items DEFAULT (0),
        StartedAtUtc     DATETIME2(3)  NULL,
        EndedAtUtc       DATETIME2(3)  NULL,
        DurationMs       BIGINT        NULL,
        HttpStatus       INT           NULL,
        PromptChars      INT           NULL,
        ResponseChars    INT           NULL,
        InputTokens      BIGINT        NULL,
        OutputTokens     BIGINT        NULL,
        ThinkingTokens   BIGINT        NULL,
        TotalTokens      BIGINT        NULL,
        EstimatedCostUsd DECIMAL(12,6) NULL,
        ParsedOk         BIT           NOT NULL CONSTRAINT DF_AiBatch_ParsedOk DEFAULT (0),
        RetryCount       INT           NOT NULL CONSTRAINT DF_AiBatch_Retry DEFAULT (0),
        ErrorMessage     NVARCHAR(400) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_AiBatch_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_AiBatch_Run ON ops.AiBatch(AiRunId);
END
GO

IF OBJECT_ID(N'ops.AuditLog', N'U') IS NULL
BEGIN
    /* Append-only: the application login gets INSERT+SELECT only —
       no UPDATE, no DELETE grants (db.md §15.5). Enforced at grant time,
       see comment at end of file. */
    CREATE TABLE ops.AuditLog(
        AuditLogId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_AuditLog PRIMARY KEY,
        CorrelationId    NVARCHAR(100) NULL,
        EntityType       NVARCHAR(60)  NOT NULL,
        EntityId         BIGINT        NULL,
        CaseId           BIGINT        NULL,
        Action           NVARCHAR(100) NOT NULL,
        ActorType        NVARCHAR(20)  NOT NULL CONSTRAINT DF_Audit_ActorType DEFAULT (N'System') CONSTRAINT CK_Audit_ActorType CHECK (ActorType IN (N'User',N'System',N'AiAgent')),
        ActorId          NVARCHAR(100) NULL,
        BeforeJson       NVARCHAR(MAX) NULL,
        AfterJson        NVARCHAR(MAX) NULL,
        IpAddress        NVARCHAR(50)  NULL,
        OccurredAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Audit_OccurredAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Audit_Entity ON ops.AuditLog(EntityType, EntityId);
    CREATE INDEX IX_Audit_Case ON ops.AuditLog(CaseId);
    CREATE INDEX IX_Audit_Correlation ON ops.AuditLog(CorrelationId);
    /* APPEND-ONLY CONTRACT: do NOT grant UPDATE/DELETE on ops.AuditLog to the
       application login. Revoked (not merely ungranted) so a future GRANT ALL
       cannot silently undo it. */
    DENY UPDATE ON ops.AuditLog TO public;
    DENY DELETE ON ops.AuditLog TO public;
END
GO

IF OBJECT_ID(N'ops.JobRun', N'U') IS NULL
BEGIN
    CREATE TABLE ops.JobRun(
        JobRunId         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_JobRun PRIMARY KEY,
        JobType          NVARCHAR(60)  NOT NULL,
        CaseId           BIGINT        NULL,
        CaseStageRunId   BIGINT        NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_JobRun_Status DEFAULT (N'Queued') CONSTRAINT CK_JobRun_Status CHECK (Status IN (N'Queued',N'Running',N'Succeeded',N'Partial',N'Failed',N'Cancelled')),
        Priority         INT           NOT NULL CONSTRAINT DF_JobRun_Priority DEFAULT (5),
        AttemptNo        INT           NOT NULL CONSTRAINT DF_JobRun_Attempt DEFAULT (1),
        MaxAttempts      INT           NOT NULL CONSTRAINT DF_JobRun_MaxAttempts DEFAULT (3),
        QueuedAtUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_JobRun_QueuedAtUtc DEFAULT (SYSUTCDATETIME()),
        StartedAtUtc     DATETIME2(3)  NULL,
        EndedAtUtc       DATETIME2(3)  NULL,
        DurationMs       BIGINT        NULL,
        ProgressPct      DECIMAL(5,2)  NULL,
        ItemsTotal       INT           NULL,
        ItemsDone        INT           NULL,
        WorkerId         NVARCHAR(100) NULL,
        IdempotencyKey   NVARCHAR(200) NULL,
        ErrorMessage     NVARCHAR(2000) NULL
    );
    CREATE INDEX IX_JobRun_Status ON ops.JobRun(Status, Priority);
    CREATE INDEX IX_JobRun_Case ON ops.JobRun(CaseId);
    CREATE UNIQUE INDEX UX_JobRun_Idempotency ON ops.JobRun(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
END
GO

IF OBJECT_ID(N'ops.Notification', N'U') IS NULL
BEGIN
    CREATE TABLE ops.Notification(
        NotificationId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_Notification PRIMARY KEY,
        RecipientUserId  INT           NULL,
        Recipient        NVARCHAR(200) NULL,
        CaseId           BIGINT        NULL,
        NotificationType NVARCHAR(40)  NOT NULL,
        Channel          NVARCHAR(10)  NOT NULL CONSTRAINT DF_Notif_Channel DEFAULT (N'InApp') CONSTRAINT CK_Notif_Channel CHECK (Channel IN (N'InApp',N'Email',N'Teams')),
        Subject          NVARCHAR(500) NULL,
        Body             NVARCHAR(MAX) NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_Notif_Status DEFAULT (N'Queued'),
        SentAtUtc        DATETIME2(3)  NULL,
        ReadAtUtc        DATETIME2(3)  NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Notif_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_Notif_Case ON ops.Notification(CaseId);
    CREATE INDEX IX_Notif_Status ON ops.Notification(Status);
END
GO

IF OBJECT_ID(N'ops.ConfigVersion', N'U') IS NULL
BEGIN
    CREATE TABLE ops.ConfigVersion(
        ConfigVersionId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_ConfigVersion PRIMARY KEY,
        Scope            NVARCHAR(30)  NOT NULL,   -- Ai, Pipeline, Thresholds, Notifications
        VersionLabel     NVARCHAR(40)  NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_CV_Status DEFAULT (N'Draft'),
        EffectiveFrom    DATETIME2(3)  NULL,
        PublishedByUserId INT          NULL,
        ApprovedByUserId INT           NULL,
        PublishedAtUtc   DATETIME2(3)  NULL
    );
    CREATE UNIQUE INDEX UX_CV_Label ON ops.ConfigVersion(Scope, VersionLabel);
END
GO

IF OBJECT_ID(N'ops.ConfigSetting', N'U') IS NULL
BEGIN
    /* Secrets are never stored here — only references to secret-store entries. */
    CREATE TABLE ops.ConfigSetting(
        ConfigSettingId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ops_ConfigSetting PRIMARY KEY,
        ConfigVersionId  INT           NULL,
        SettingKey       NVARCHAR(200) NOT NULL,
        SettingValue     NVARCHAR(MAX) NULL,
        ValueType        NVARCHAR(30)  NOT NULL CONSTRAINT DF_CS_ValueType DEFAULT (N'String'),
        Description      NVARCHAR(500) NULL,
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_CS_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_CS_Key ON ops.ConfigSetting(ConfigVersionId, SettingKey);
END
GO
PRINT '07_ops done';
GO
