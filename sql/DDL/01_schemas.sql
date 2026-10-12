SET QUOTED_IDENTIFIER ON;
GO
/* =====================================================================
   GMC v2 — 01_schemas.sql
   Creates the six schemas (db.md §2.1). Idempotent.
   Target: SQL Server 2022, database GMCv2.
   ===================================================================== */
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'core')
    EXEC(N'CREATE SCHEMA core;');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ing')
    EXEC(N'CREATE SCHEMA ing;');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'uw')
    EXEC(N'CREATE SCHEMA uw;');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'kb')
    EXEC(N'CREATE SCHEMA kb;');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ref')
    EXEC(N'CREATE SCHEMA ref;');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ops')
    EXEC(N'CREATE SCHEMA ops;');
GO
PRINT '01_schemas done';
GO
