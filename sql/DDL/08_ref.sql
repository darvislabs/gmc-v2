SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 08_ref.sql — reference data and identity (db.md §13, §14).
   ref.User stores hash + salt ONLY — never plaintext (db.md §14).
   Idempotent.
   ===================================================================== */

IF OBJECT_ID(N'ref.[User]', N'U') IS NULL
BEGIN
    CREATE TABLE ref.[User](
        UserId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_User PRIMARY KEY,
        UserName         NVARCHAR(100) NOT NULL,
        DisplayName      NVARCHAR(200) NULL,
        Email            NVARCHAR(200) NULL,
        Mobile           NVARCHAR(30)  NULL,
        ExternalIdpSubject NVARCHAR(200) NULL,
        PasswordHash     NVARCHAR(256) NULL,   -- fallback auth only; PBKDF2/bcrypt output
        PasswordSalt     NVARCHAR(128) NULL,   -- per-user random salt; NEVER plaintext
        IsActive         BIT           NOT NULL CONSTRAINT DF_Usr_IsActive DEFAULT (1),
        LastLoginAtUtc   DATETIME2(3)  NULL,
        CreatedBy        NVARCHAR(100) NOT NULL CONSTRAINT DF_Usr_CreatedBy DEFAULT (N'System'),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Usr_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Usr_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        RowVersion       ROWVERSION
    );
    CREATE UNIQUE INDEX UX_User_UserName ON ref.[User](UserName);
END
GO

IF OBJECT_ID(N'ref.Role', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Role(
        RoleId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Role PRIMARY KEY,
        RoleName         NVARCHAR(50)  NOT NULL,   -- Admin, SalesPerson, Underwriter, SeniorUnderwriter, ChiefUnderwriter, Actuarial, Auditor
        Description      NVARCHAR(500) NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Role_IsActive DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_Role_RoleName ON ref.Role(RoleName);
END
GO

IF OBJECT_ID(N'ref.Permission', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Permission(
        PermissionId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Permission PRIMARY KEY,
        PermissionCode   NVARCHAR(100) NOT NULL,   -- Case.Create, Mapping.Approve, Quote.Publish, Pricing.Override, Exception.Waive, Admin.Masters ...
        Description      NVARCHAR(500) NULL
    );
    CREATE UNIQUE INDEX UX_Permission_Code ON ref.Permission(PermissionCode);
END
GO

IF OBJECT_ID(N'ref.UserRole', N'U') IS NULL
BEGIN
    CREATE TABLE ref.UserRole(
        UserId           INT NOT NULL,
        RoleId           INT NOT NULL,
        CONSTRAINT PK_ref_UserRole PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRole_User FOREIGN KEY (UserId) REFERENCES ref.[User](UserId),
        CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleId) REFERENCES ref.Role(RoleId)
    );
END
GO

IF OBJECT_ID(N'ref.RolePermission', N'U') IS NULL
BEGIN
    CREATE TABLE ref.RolePermission(
        RoleId           INT NOT NULL,
        PermissionId     INT NOT NULL,
        CanView          BIT NOT NULL CONSTRAINT DF_RP_View DEFAULT (1),
        CanEdit          BIT NOT NULL CONSTRAINT DF_RP_Edit DEFAULT (0),
        CONSTRAINT PK_ref_RolePermission PRIMARY KEY (RoleId, PermissionId),
        CONSTRAINT FK_RolePermission_Role FOREIGN KEY (RoleId) REFERENCES ref.Role(RoleId),
        CONSTRAINT FK_RolePermission_Permission FOREIGN KEY (PermissionId) REFERENCES ref.Permission(PermissionId)
    );
END
GO

IF OBJECT_ID(N'ref.UserDelegation', N'U') IS NULL
BEGIN
    CREATE TABLE ref.UserDelegation(
        DelegationId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_UserDelegation PRIMARY KEY,
        FromUserId       INT           NOT NULL,
        ToUserId         INT           NOT NULL,
        ValidFrom        DATETIME2(3)  NOT NULL,
        ValidTo          DATETIME2(3)  NULL,
        Reason           NVARCHAR(500) NULL
    );
END
GO

IF OBJECT_ID(N'ref.Insurer', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Insurer(
        InsurerId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Insurer PRIMARY KEY,
        InsurerName      NVARCHAR(200) NOT NULL,
        ShortCode        NVARCHAR(30)  NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Ins_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Ins_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Insurer_Name ON ref.Insurer(InsurerName);
END
GO

IF OBJECT_ID(N'ref.Tpa', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Tpa(
        TpaId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Tpa PRIMARY KEY,
        TpaName          NVARCHAR(200) NOT NULL,
        ShortCode        NVARCHAR(30)  NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_Tpa_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Tpa_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Tpa_Name ON ref.Tpa(TpaName);
END
GO

IF OBJECT_ID(N'ref.Industry', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Industry(
        IndustryId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Industry PRIMARY KEY,
        IndustryName     NVARCHAR(200) NOT NULL,
        RiskCategory     NVARCHAR(30)  NULL,
        OccupationRiskFactor DECIMAL(9,6) NULL,
        IsRestricted     BIT           NOT NULL CONSTRAINT DF_Ind_IsRestricted DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_Ind_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Ind_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_Industry_Name ON ref.Industry(IndustryName);
END
GO

IF OBJECT_ID(N'ref.FinancialYear', N'U') IS NULL
BEGIN
    CREATE TABLE ref.FinancialYear(
        FinancialYear    NVARCHAR(7)   NOT NULL CONSTRAINT PK_ref_FinancialYear PRIMARY KEY,  -- e.g. 2026-27
        StartDate        DATE          NOT NULL,
        EndDate          DATE          NOT NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_FY_IsActive DEFAULT (1)
    );
END
GO

IF OBJECT_ID(N'ref.PaidStatusDefinition', N'U') IS NULL
BEGIN
    CREATE TABLE ref.PaidStatusDefinition(
        PaidStatusDefinitionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_PaidStatusDefinition PRIMARY KEY,
        RawStatusText    NVARCHAR(100) NOT NULL,
        NormalizedStatus NVARCHAR(20)  NOT NULL CONSTRAINT CK_PSD_Normalized CHECK (NormalizedStatus IN (N'Paid',N'Outstanding',N'Closed',N'Rejected')),
        InsurerId        INT           NULL,     -- scoping; null = global
        TpaId            INT           NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_PSD_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_PSD_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_PSD_Match ON ref.PaidStatusDefinition(RawStatusText, InsurerId, TpaId) WHERE IsActive = 1;
END
GO

IF OBJECT_ID(N'ref.Geography', N'U') IS NULL
BEGIN
    CREATE TABLE ref.Geography(
        GeographyId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_Geography PRIMARY KEY,
        City             NVARCHAR(100) NOT NULL,
        State            NVARCHAR(100) NULL,
        Zone             NVARCHAR(30)  NULL,
        RiskTier         NVARCHAR(20)  NULL,
        IsRestricted     BIT           NOT NULL CONSTRAINT DF_Geo_IsRestricted DEFAULT (0),
        IsActive         BIT           NOT NULL CONSTRAINT DF_Geo_IsActive DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_Geo_City ON ref.Geography(City, State);
END
GO

IF OBJECT_ID(N'ref.HighRiskDisease', N'U') IS NULL
BEGIN
    CREATE TABLE ref.HighRiskDisease(
        HighRiskDiseaseId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_HighRiskDisease PRIMARY KEY,
        IcdCode          VARCHAR(20)   NOT NULL,
        ProcedureName    NVARCHAR(300) NULL,
        RiskTier         NVARCHAR(20)  NULL,
        RequiresReferral BIT           NOT NULL CONSTRAINT DF_HRD_RequiresReferral DEFAULT (1),
        IsActive         BIT           NOT NULL CONSTRAINT DF_HRD_IsActive DEFAULT (1)
    );
END
GO

IF OBJECT_ID(N'ref.NonStandardBenefit', N'U') IS NULL
BEGIN
    CREATE TABLE ref.NonStandardBenefit(
        NonStandardBenefitId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_NonStandardBenefit PRIMARY KEY,
        BenefitCode      NVARCHAR(40)  NOT NULL,
        BenefitName      NVARCHAR(200) NULL,
        RequiresReferral BIT           NOT NULL CONSTRAINT DF_NSB_RequiresReferral DEFAULT (1),
        Notes            NVARCHAR(1000) NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_NSB_IsActive DEFAULT (1)
    );
END
GO

IF OBJECT_ID(N'ref.ClaimSeverityBand', N'U') IS NULL
BEGIN
    CREATE TABLE ref.ClaimSeverityBand(
        BandId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ref_ClaimSeverityBand PRIMARY KEY,
        MinAmount        DECIMAL(18,2) NOT NULL,
        MaxAmount        DECIMAL(18,2) NULL,
        BandLabel        NVARCHAR(50)  NOT NULL,
        DisplayOrder     INT           NOT NULL CONSTRAINT DF_CSB_DisplayOrder DEFAULT (0)
    );
END
GO
PRINT '08_ref done';
GO
