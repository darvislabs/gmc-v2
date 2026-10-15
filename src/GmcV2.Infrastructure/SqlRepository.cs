using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GmcV2.Core;
using Microsoft.Data.SqlClient;

namespace GmcV2.Infrastructure;

/// <summary>
/// SQL Server implementation of IGmcRepository against the sql/DDL scripts
/// (schemas core/ing/uw/kb/ref/ops). ADO.NET only, parameterized SQL.
/// JSON columns (DetailJson, MetricsJson, ComponentsJson, ...) are
/// serialized with System.Text.Json.
/// </summary>
public sealed partial class SqlRepository : IGmcRepository
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public SqlRepository(string connectionString)
    {
        _connectionString = connectionString;
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new ArgumentException("SQL connection string must be provided.", nameof(connectionString));
    }

    // ─────────────────────────── helpers ───────────────────────────

    private async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static SqlCommand Cmd(SqlConnection c, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private static string S(object? o) => o is DBNull or null ? "" : (string)o;
    private static string? SN(object? o) => o is DBNull or null ? null : (string)o;
    private static long L(object? o) => o is DBNull or null ? 0 : Convert.ToInt64(o);
    private static long? LN(object? o) => o is DBNull or null ? null : Convert.ToInt64(o);
    private static int I(object? o) => o is DBNull or null ? 0 : Convert.ToInt32(o);
    private static int? IN(object? o) => o is DBNull or null ? null : Convert.ToInt32(o);
    private static decimal D(object? o) => o is DBNull or null ? 0m : Convert.ToDecimal(o);
    private static decimal? DN(object? o) => o is DBNull or null ? null : Convert.ToDecimal(o);
    private static bool B(object? o) => !(o is DBNull or null) && Convert.ToBoolean(o);
    private static DateTime T(object? o) => o is DBNull or null ? DateTime.MinValue : Convert.ToDateTime(o);
    private static DateTime? TN(object? o) => o is DBNull or null ? null : Convert.ToDateTime(o);

    private static string J<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);
    private static T? Jd<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOpts);

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    private static string[] JArr(object? o) =>
        Jd<string[]>(SN(o)) ?? Array.Empty<string>();

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    // ─────────────────────────── cases ───────────────────────────

    public async Task<Case> CreateCaseAsync(Case c)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        // Indian financial year, April–March, in yy-yy form (2026-27 → "2627").
        var now = c.CreatedAtUtc == default ? DateTime.UtcNow : c.CreatedAtUtc;
        var fyStartYear = now.Month >= 4 ? now.Year : now.Year - 1;
        var fy = $"{fyStartYear % 100:00}{(fyStartYear + 1) % 100:00}";

        // Per-FY sequence with UPDLOCK/HOLDLOCK so concurrent creates serialize.
        var seqCmd = Cmd(conn, """
            UPDATE core.CaseNumberSeq WITH (UPDLOCK, HOLDLOCK)
               SET LastSeq = LastSeq + 1, UpdatedAtUtc = SYSUTCDATETIME()
             WHERE FinancialYear = @fy;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT core.CaseNumberSeq (FinancialYear, LastSeq) VALUES (@fy, 1);
                SELECT CAST(1 AS BIGINT);
            END
            ELSE SELECT CAST(LastSeq AS BIGINT) FROM core.CaseNumberSeq WHERE FinancialYear = @fy;
            """);
        seqCmd.Transaction = tx;
        seqCmd.Parameters.AddWithValue("@fy", fy);
        var seq = Convert.ToInt64((await seqCmd.ExecuteScalarAsync())!);

        c.CaseNumber = $"GMC-C-{fy}-{seq:D5}";

        using var cmd = Cmd(conn, """
            INSERT core.[Case] (CaseNo, CorporateName, BusinessType, BusinessSubType, FinancialYear,
                                Status, CurrentStage, CurrentStageAttempt, SourceSystem,
                                CreatedBy, CreatedAtUtc, UpdatedAtUtc)
            OUTPUT INSERTED.CaseId
            VALUES (@no, @corp, @biz, @sub, @fy2, @status, @stage, @attempt, N'GMCv2', @by, @created, @updated)
            """);
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@no", c.CaseNumber);
        cmd.Parameters.AddWithValue("@corp", c.ClientName);
        cmd.Parameters.AddWithValue("@biz", c.BusinessType);
        cmd.Parameters.AddWithValue("@sub", (object?)c.BusinessSubType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fy2", fy);
        cmd.Parameters.AddWithValue("@status", c.Status);
        cmd.Parameters.AddWithValue("@stage", (object?)c.CurrentStage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@attempt", c.CurrentStageAttempt);
        cmd.Parameters.AddWithValue("@by", c.CreatedBy);
        cmd.Parameters.AddWithValue("@created", now);
        cmd.Parameters.AddWithValue("@updated", now);
        c.CaseId = (long)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return c;
    }

    private static Case MapCase(SqlDataReader r) => new()
    {
        CaseId = L(r["CaseId"]),
        CaseNumber = S(r["CaseNo"]),
        ClientName = S(r["CorporateName"]),
        BusinessType = S(r["BusinessType"]),
        BusinessSubType = SN(r["BusinessSubType"]) ?? "Main",
        Status = S(r["Status"]),
        CurrentStage = SN(r["CurrentStage"]),
        CurrentStageAttempt = I(r["CurrentStageAttempt"]),
        CreatedBy = S(r["CreatedBy"]),
        CreatedAtUtc = T(r["CreatedAtUtc"]),
        UpdatedAtUtc = T(r["UpdatedAtUtc"]),
    };

    private const string CaseSelect = """
        SELECT CaseId, CaseNo, CorporateName, BusinessType, BusinessSubType, Status,
               CurrentStage, CurrentStageAttempt, CreatedBy, CreatedAtUtc, UpdatedAtUtc
          FROM core.[Case]
        """;

    public async Task<Case?> GetCaseAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        Case c;
        using (var cmd = Cmd(conn, CaseSelect + " WHERE CaseId = @id"))
        {
            cmd.Parameters.AddWithValue("@id", caseId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            c = MapCase(r);
        }
        await LoadCaseChildrenAsync(conn, c);
        return c;
    }

    public async Task<Case?> GetCaseByNumberAsync(string caseNumber)
    {
        await using var conn = await OpenAsync();
        Case c;
        using (var cmd = Cmd(conn, CaseSelect + " WHERE CaseNo = @no"))
        {
            cmd.Parameters.AddWithValue("@no", caseNumber);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            c = MapCase(r);
        }
        await LoadCaseChildrenAsync(conn, c);
        return c;
    }

    private static async Task LoadCaseChildrenAsync(SqlConnection conn, Case c)
    {
        using (var cmd = Cmd(conn, "SELECT DocumentId, CaseId, DocType, FileName, StorageUri, ParseStatus, Sha256, UploadedAtUtc FROM core.CaseDocument WHERE CaseId = @id ORDER BY DocumentId"))
        {
            cmd.Parameters.AddWithValue("@id", c.CaseId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                c.Documents.Add(new CaseDocument
                {
                    DocumentId = L(r["DocumentId"]),
                    CaseId = L(r["CaseId"]),
                    DocType = S(r["DocType"]),
                    FileName = S(r["FileName"]),
                    StorageUri = S(r["StorageUri"]),
                    ParseStatus = S(r["ParseStatus"]),
                    Sha256 = S(r["Sha256"]),
                    UploadedAtUtc = T(r["UploadedAtUtc"]),
                });
        }
        using (var cmd = Cmd(conn, "SELECT CaseStageRunId, CaseId, Stage, AttemptNo, State, InputsHash, DetailJson, StartedAtUtc, EndedAtUtc, DurationMs FROM core.CaseStageRun WHERE CaseId = @id ORDER BY CaseStageRunId"))
        {
            cmd.Parameters.AddWithValue("@id", c.CaseId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                c.StageRuns.Add(new CaseStageRun
                {
                    StageRunId = L(r["CaseStageRunId"]),
                    CaseId = L(r["CaseId"]),
                    Stage = S(r["Stage"]),
                    AttemptNo = I(r["AttemptNo"]),
                    State = S(r["State"]),
                    InputsHash = SN(r["InputsHash"]),
                    DetailJson = SN(r["DetailJson"]),
                    StartedAtUtc = TN(r["StartedAtUtc"]),
                    CompletedAtUtc = TN(r["EndedAtUtc"]),
                    ElapsedSeconds = DN(r["DurationMs"]) / 1000m,
                });
        }
        using (var cmd = Cmd(conn, "SELECT CaseExceptionId, CaseId, ExceptionCode, Severity, Status, SubjectType, SubjectRef, Reason, DetailJson, ResolutionTarget, ResumeStage, ResolvedBy, ResolvedAtUtc FROM core.CaseException WHERE CaseId = @id ORDER BY CaseExceptionId"))
        {
            cmd.Parameters.AddWithValue("@id", c.CaseId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                c.Exceptions.Add(MapException(r));
        }
    }

    public async Task UpdateCaseAsync(Case c)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            UPDATE core.[Case]
               SET CorporateName = @corp, BusinessType = @biz, BusinessSubType = @sub,
                   Status = @status, CurrentStage = @stage, CurrentStageAttempt = @attempt,
                   QuoteConfidence = @conf,
                   IsAutoQuoted = CASE WHEN @status = 'AutoQuoted' THEN 1 ELSE 0 END,
                   UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @by
             WHERE CaseId = @id
            """);
        cmd.Parameters.AddWithValue("@id", c.CaseId);
        cmd.Parameters.AddWithValue("@corp", c.ClientName);
        cmd.Parameters.AddWithValue("@biz", c.BusinessType);
        cmd.Parameters.AddWithValue("@sub", (object?)c.BusinessSubType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", c.Status);
        cmd.Parameters.AddWithValue("@stage", (object?)c.CurrentStage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@attempt", c.CurrentStageAttempt);
        cmd.Parameters.AddWithValue("@conf", DBNull.Value); // QuoteConfidence lives on uw.QuoteVersion, not core.Case (Core model)
        cmd.Parameters.AddWithValue("@by", "Pipeline");
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Case>> ListCasesAsync(string? status = null)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, CaseSelect + (string.IsNullOrEmpty(status) ? "" : " WHERE Status = @status") + " ORDER BY CaseId DESC");
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Case>();
        while (await r.ReadAsync()) list.Add(MapCase(r));
        return list;
    }

    // ─────────────────────────── documents ───────────────────────────

    public async Task<CaseDocument> AddDocumentAsync(CaseDocument d)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT core.CaseDocument (CaseId, DocType, FileName, StorageUri, ContentType, SizeBytes, Sha256,
                                      ScanStatus, ParseStatus, UploadedBy, UploadedAtUtc)
            OUTPUT INSERTED.DocumentId
            VALUES (@case, @type, @name, @uri, N'application/octet-stream', 0, @sha, N'Clean', @parse, N'System', SYSUTCDATETIME())
            """);
        cmd.Parameters.AddWithValue("@case", d.CaseId);
        cmd.Parameters.AddWithValue("@type", d.DocType);
        cmd.Parameters.AddWithValue("@name", d.FileName);
        cmd.Parameters.AddWithValue("@uri", d.StorageUri);
        cmd.Parameters.AddWithValue("@sha", string.IsNullOrEmpty(d.Sha256) ? Hash(d.FileName + "|" + d.StorageUri) : d.Sha256);
        cmd.Parameters.AddWithValue("@parse", d.ParseStatus);
        d.DocumentId = (long)(await cmd.ExecuteScalarAsync())!;
        return d;
    }

    public async Task<CaseDocument?> GetDocumentAsync(long documentId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT DocumentId, CaseId, DocType, FileName, StorageUri, ParseStatus, Sha256, UploadedAtUtc FROM core.CaseDocument WHERE DocumentId = @id");
        cmd.Parameters.AddWithValue("@id", documentId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new CaseDocument
        {
            DocumentId = L(r["DocumentId"]),
            CaseId = L(r["CaseId"]),
            DocType = S(r["DocType"]),
            FileName = S(r["FileName"]),
            StorageUri = S(r["StorageUri"]),
            ParseStatus = S(r["ParseStatus"]),
            Sha256 = S(r["Sha256"]),
            UploadedAtUtc = T(r["UploadedAtUtc"]),
        };
    }

    public async Task UpdateDocumentAsync(CaseDocument d)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "UPDATE core.CaseDocument SET ParseStatus = @parse, Sha256 = @sha, FileName = @name, StorageUri = @uri, UpdatedAtUtc = SYSUTCDATETIME() WHERE DocumentId = @id");
        cmd.Parameters.AddWithValue("@id", d.DocumentId);
        cmd.Parameters.AddWithValue("@parse", d.ParseStatus);
        cmd.Parameters.AddWithValue("@sha", string.IsNullOrEmpty(d.Sha256) ? Hash(d.FileName + "|" + d.StorageUri) : d.Sha256);
        cmd.Parameters.AddWithValue("@name", d.FileName);
        cmd.Parameters.AddWithValue("@uri", d.StorageUri);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── stage runs ───────────────────────────

    public async Task<CaseStageRun> StartStageRunAsync(long caseId, string stage, int attemptNo, string inputsHash)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT core.CaseStageRun (CaseId, Stage, AttemptNo, InputsHash, State, StartedAtUtc, TriggeredBy)
            OUTPUT INSERTED.CaseStageRunId
            VALUES (@case, @stage, @attempt, @hash, N'Running', SYSUTCDATETIME(), N'Pipeline')
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@stage", stage);
        cmd.Parameters.AddWithValue("@attempt", attemptNo);
        cmd.Parameters.AddWithValue("@hash", inputsHash);
        return new CaseStageRun
        {
            StageRunId = (long)(await cmd.ExecuteScalarAsync())!,
            CaseId = caseId,
            Stage = stage,
            AttemptNo = attemptNo,
            State = Enums.StageRunning,
            InputsHash = inputsHash,
            StartedAtUtc = DateTime.UtcNow,
        };
    }

    public async Task<CaseStageRun?> GetLatestStageRunAsync(long caseId, string stage)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT TOP (1) CaseStageRunId, CaseId, Stage, AttemptNo, State, InputsHash, DetailJson, StartedAtUtc, EndedAtUtc, DurationMs
              FROM core.CaseStageRun WHERE CaseId = @case AND Stage = @stage
             ORDER BY AttemptNo DESC, CaseStageRunId DESC
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@stage", stage);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new CaseStageRun
        {
            StageRunId = L(r["CaseStageRunId"]),
            CaseId = L(r["CaseId"]),
            Stage = S(r["Stage"]),
            AttemptNo = I(r["AttemptNo"]),
            State = S(r["State"]),
            InputsHash = SN(r["InputsHash"]),
            DetailJson = SN(r["DetailJson"]),
            StartedAtUtc = TN(r["StartedAtUtc"]),
            CompletedAtUtc = TN(r["EndedAtUtc"]),
            ElapsedSeconds = DN(r["DurationMs"]) / 1000m,
        };
    }

    public async Task CompleteStageRunAsync(long stageRunId, string state, string? detailJson, decimal elapsedSeconds)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            UPDATE core.CaseStageRun
               SET State = @state, DetailJson = @detail, EndedAtUtc = SYSUTCDATETIME(), DurationMs = @ms
             WHERE CaseStageRunId = @id
            """);
        cmd.Parameters.AddWithValue("@id", stageRunId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@detail", (object?)detailJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ms", elapsedSeconds * 1000m);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── exceptions ───────────────────────────

    private static CaseException MapException(SqlDataReader r) => new()
    {
        ExceptionId = L(r["CaseExceptionId"]),
        CaseId = L(r["CaseId"]),
        Code = S(r["ExceptionCode"]),
        Blocking = S(r["Severity"]) == "Blocking",
        Status = S(r["Status"]),
        SubjectType = S(r["SubjectType"]),
        SubjectRef = S(r["SubjectRef"]),
        Message = S(r["Reason"]),
        DetailJson = SN(r["DetailJson"]),
        ResolutionTarget = S(r["ResolutionTarget"]),
        ResumeStage = S(r["ResumeStage"]),
        ResolvedBy = SN(r["ResolvedBy"]),
        ResolvedAtUtc = TN(r["ResolvedAtUtc"]),
    };

    private const string ExceptionSelect = """
        SELECT CaseExceptionId, CaseId, ExceptionCode, Severity, Status, SubjectType, SubjectRef,
               Reason, DetailJson, ResolutionTarget, ResumeStage, ResolvedBy, ResolvedAtUtc
          FROM core.CaseException
        """;

    public async Task<CaseException> RaiseExceptionAsync(CaseException e)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT core.CaseException (CaseId, ExceptionCode, Severity, Stage, SubjectType, SubjectRef, Reason,
                                       DetailJson, ResolutionTarget, ResumeStage, Status)
            OUTPUT INSERTED.CaseExceptionId
            VALUES (@case, @code, @sev, @stage, @stype, @sref, @reason, @detail, @rtarget, @rstage, N'Open')
            """);
        cmd.Parameters.AddWithValue("@case", e.CaseId);
        cmd.Parameters.AddWithValue("@code", e.Code);
        cmd.Parameters.AddWithValue("@sev", e.Blocking ? "Blocking" : "Advisory");
        cmd.Parameters.AddWithValue("@stage", e.ResumeStage is { Length: > 0 } ? e.ResumeStage : "Pipeline");
        cmd.Parameters.AddWithValue("@stype", (object?)e.SubjectType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sref", (object?)e.SubjectRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", e.Message);
        cmd.Parameters.AddWithValue("@detail", (object?)e.DetailJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rtarget", (object?)e.ResolutionTarget ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rstage", (object?)e.ResumeStage ?? DBNull.Value);
        e.ExceptionId = (long)(await cmd.ExecuteScalarAsync())!;
        return e;
    }

    public async Task<List<CaseException>> GetOpenExceptionsAsync(long caseId, bool blockingOnly = false)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, ExceptionSelect +
            " WHERE CaseId = @case AND Status IN (N'Open', N'Assigned')" +
            (blockingOnly ? " AND Severity = N'Blocking'" : "") + " ORDER BY CaseExceptionId");
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CaseException>();
        while (await r.ReadAsync()) list.Add(MapException(r));
        return list;
    }

    public async Task<CaseException?> GetExceptionAsync(long exceptionId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, ExceptionSelect + " WHERE CaseExceptionId = @id");
        cmd.Parameters.AddWithValue("@id", exceptionId);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapException(r) : null;
    }

    public async Task ResolveExceptionAsync(long exceptionId, string resolvedBy)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "UPDATE core.CaseException SET Status = N'Resolved', ResolvedBy = @by, ResolvedAtUtc = SYSUTCDATETIME() WHERE CaseExceptionId = @id");
        cmd.Parameters.AddWithValue("@id", exceptionId);
        cmd.Parameters.AddWithValue("@by", resolvedBy);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────── notes / notifications ───────────────────────

    public async Task AddNoteAsync(long caseId, string author, string text)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "INSERT core.CaseNote (CaseId, Author, Body) VALUES (@case, @author, @body)");
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@author", author);
        cmd.Parameters.AddWithValue("@body", text);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── quote versions ───────────────────────────

    public async Task AddQuoteArtifactAsync(long quoteVersionId, string artifactType, string fileName, byte[] content, string generatedBy)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT uw.QuoteArtifact (QuoteVersionId, ArtifactType, StorageUri, FileName, SizeBytes, Sha256, GeneratedAtUtc, GeneratedBy)
            VALUES (@qv, @type, @uri, @file, @size, @sha, SYSUTCDATETIME(), @by)
            """);
        cmd.Parameters.AddWithValue("@qv", quoteVersionId);
        cmd.Parameters.AddWithValue("@type", artifactType);
        cmd.Parameters.AddWithValue("@uri", "db://quoteartifacts/" + quoteVersionId + "/" + fileName);
        cmd.Parameters.AddWithValue("@file", fileName);
        cmd.Parameters.AddWithValue("@size", content.LongLength);
        cmd.Parameters.AddWithValue("@sha", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))[..32]);
        cmd.Parameters.AddWithValue("@by", generatedBy);
        await cmd.ExecuteNonQueryAsync();

        // payload (read-back support)
        using (var contentCmd = Cmd(conn, """
            IF OBJECT_ID(N'uw.QuoteArtifactContent', N'U') IS NULL
            CREATE TABLE uw.QuoteArtifactContent(
                QuoteArtifactId BIGINT NOT NULL CONSTRAINT PK_QAC PRIMARY KEY,
                Content NVARCHAR(MAX) NOT NULL);
            """))
            await contentCmd.ExecuteNonQueryAsync();
        using (var ins = Cmd(conn, "INSERT uw.QuoteArtifactContent (QuoteArtifactId, Content) VALUES (SCOPE_IDENTITY(), @content)"))
        {
            ins.Parameters.AddWithValue("@content", Encoding.UTF8.GetString(content));
            await ins.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<QuoteArtifactInfo>> GetQuoteArtifactsAsync(long quoteVersionId)
    {
        var list = new List<QuoteArtifactInfo>();
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT QuoteArtifactId, QuoteVersionId, ArtifactType, FileName, SizeBytes, Sha256, GeneratedAtUtc, GeneratedBy FROM uw.QuoteArtifact WHERE QuoteVersionId = @qv ORDER BY QuoteArtifactId");
        cmd.Parameters.AddWithValue("@qv", quoteVersionId);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new QuoteArtifactInfo
            {
                QuoteArtifactId = L(r["QuoteArtifactId"]),
                QuoteVersionId = L(r["QuoteVersionId"]),
                ArtifactType = S(r["ArtifactType"]),
                FileName = S(r["FileName"]),
                SizeBytes = L(r["SizeBytes"]),
                Sha256 = SN(r["Sha256"]),
                GeneratedAtUtc = T(r["GeneratedAtUtc"]),
                GeneratedBy = SN(r["GeneratedBy"]),
            });
        return list;
    }

    public async Task<(string Type, string Content)?> GetQuoteArtifactContentAsync(long artifactId)
    {
        // content lives in uw.QuoteArtifactContent (created on demand below)
        await using var conn = await OpenAsync();
        using (var ensure = Cmd(conn, """
            IF OBJECT_ID(N'uw.QuoteArtifactContent', N'U') IS NULL
            CREATE TABLE uw.QuoteArtifactContent(
                QuoteArtifactId BIGINT NOT NULL CONSTRAINT PK_QAC PRIMARY KEY,
                Content NVARCHAR(MAX) NOT NULL);
            """))
                await ensure.ExecuteNonQueryAsync();
        using var cmd = Cmd(conn, "SELECT a.ArtifactType, c.Content FROM uw.QuoteArtifact a JOIN uw.QuoteArtifactContent c ON c.QuoteArtifactId = a.QuoteArtifactId WHERE a.QuoteArtifactId = @id");
        cmd.Parameters.AddWithValue("@id", artifactId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (S(r["ArtifactType"]), S(r["Content"]));
    }

    public async Task<QuoteVersion> AddQuoteVersionAsync(QuoteVersion q)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        // QuoteNo: GMC-Q-{FY}-{seq}-V{n}, FY taken from the case number.
        var caseRow = Cmd(conn, "SELECT CaseNo FROM core.[Case] WHERE CaseId = @id");
        caseRow.Transaction = tx;
        caseRow.Parameters.AddWithValue("@id", q.CaseId);
        var caseNo = S(await caseRow.ExecuteScalarAsync());
        var fyPart = caseNo.StartsWith("GMC-C-", StringComparison.Ordinal) && caseNo.Length >= 10
            ? caseNo.Substring(6, 4) : "0000";

        var seqCmd = Cmd(conn, """
            UPDATE core.CaseNumberSeq WITH (UPDLOCK, HOLDLOCK)
               SET LastSeq = LastSeq + 1 WHERE FinancialYear = @fy;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT core.CaseNumberSeq (FinancialYear, LastSeq) VALUES (@fy, 1);
                SELECT CAST(1 AS BIGINT);
            END
            ELSE SELECT CAST(LastSeq AS BIGINT) FROM core.CaseNumberSeq WHERE FinancialYear = @fy;
            """);
        seqCmd.Transaction = tx;
        seqCmd.Parameters.AddWithValue("@fy", "Q" + fyPart);
        var seq = Convert.ToInt64((await seqCmd.ExecuteScalarAsync())!);
        var quoteNo = $"GMC-Q-{fyPart}-{seq:D5}-V{q.VersionNo}";

        // Exactly one Current per case: supersede any existing Current inside the transaction.
        if (q.Status == Enums.QuoteCurrent)
        {
            using var sup = Cmd(conn, """
                UPDATE uw.QuoteVersion SET Status = N'Superseded', UpdatedAtUtc = SYSUTCDATETIME()
                 WHERE CaseId = @case AND Status = N'Current'
                """);
            sup.Transaction = tx;
            sup.Parameters.AddWithValue("@case", q.CaseId);
            await sup.ExecuteNonQueryAsync();
        }

        var lives = q.Analytics?.CoveredLives ?? 0;
        var net = q.Pricing?.TechnicalPremium ?? q.GrossPremium;
        var gstAmount = Math.Round(net * 0.18m, 2, MidpointRounding.AwayFromZero);
        var grossBeforeGst = q.GrossPremium > 0 ? q.GrossPremium : net;

        using var cmd = Cmd(conn, """
            INSERT uw.QuoteVersion (CaseId, VersionNo, QuoteNo, Status, GenerationMode, RevisionReason, RevisionNotes,
                                    SupersedesQuoteVersionId, CoveredLives, TechnicalPremium, CommissionPct,
                                    PremiumBeforeGst, GstRatePct, GstAmount, TotalPremiumInclGst, RatePerLifeBeforeGst,
                                    QuoteConfidence, IsAutoQuote, SnapshotJson, InputsHash,
                                    PricingJson, AnalyticsJson, GuardrailsJson, BenefitDeltaJson,
                                    CreatedBy, CreatedAtUtc, UpdatedAtUtc)
            OUTPUT INSERTED.QuoteVersionId
            VALUES (@case, @vno, @qno, @status, @mode, @rev, @revnotes, @sup, @lives, @tech, @comm,
                    @net, @gst, @gsta, @total, @rate, @conf, @auto, @snap, @hash, @pj, @aj, @gj, @bd, @by, SYSUTCDATETIME(), SYSUTCDATETIME())
            """);
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@case", q.CaseId);
        cmd.Parameters.AddWithValue("@vno", q.VersionNo);
        cmd.Parameters.AddWithValue("@qno", quoteNo);
        cmd.Parameters.AddWithValue("@status", q.Status);
        cmd.Parameters.AddWithValue("@mode", q.GenerationMode);
        cmd.Parameters.AddWithValue("@rev", (object?)q.RevisionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@revnotes", (object?)q.RevisionNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sup", (object?)q.SupersedesQuoteVersionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lives", lives);
        cmd.Parameters.AddWithValue("@tech", net);
        cmd.Parameters.AddWithValue("@comm", 7.5m);
        cmd.Parameters.AddWithValue("@net", grossBeforeGst);
        cmd.Parameters.AddWithValue("@gst", 18m);
        cmd.Parameters.AddWithValue("@gsta", gstAmount);
        cmd.Parameters.AddWithValue("@total", grossBeforeGst + gstAmount);
        cmd.Parameters.AddWithValue("@rate", lives > 0 ? Math.Round(grossBeforeGst / lives, 2, MidpointRounding.AwayFromZero) : 0m);
        cmd.Parameters.AddWithValue("@conf", (object?)q.Guardrails?.Confidence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@auto", q.Guardrails is null || q.Guardrails.Outcome == "AutoQuote");
        cmd.Parameters.AddWithValue("@snap", J(new { q.VersionNo, q.Status, q.GenerationMode, q.GrossPremium }));
        cmd.Parameters.AddWithValue("@hash", (object?)q.InputsHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pj", q.Pricing is null ? DBNull.Value : J(q.Pricing));
        cmd.Parameters.AddWithValue("@aj", q.Analytics is null ? DBNull.Value : J(q.Analytics));
        cmd.Parameters.AddWithValue("@gj", q.Guardrails is null ? DBNull.Value : J(q.Guardrails));
        cmd.Parameters.AddWithValue("@bd", q.BenefitDelta.Count == 0 ? DBNull.Value : J(q.BenefitDelta));
        cmd.Parameters.AddWithValue("@by", q.CreatedBy);
        q.QuoteVersionId = (long)(await cmd.ExecuteScalarAsync())!;

        if (q.Status == Enums.QuoteCurrent)
        {
            using var link = Cmd(conn, "UPDATE core.[Case] SET CurrentQuoteVersionId = @qvid, UpdatedAtUtc = SYSUTCDATETIME() WHERE CaseId = @case");
            link.Transaction = tx;
            link.Parameters.AddWithValue("@qvid", q.QuoteVersionId);
            link.Parameters.AddWithValue("@case", q.CaseId);
            await link.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return q;
    }

    private static QuoteVersion MapQuoteVersion(SqlDataReader r) => new()
    {
        QuoteVersionId = L(r["QuoteVersionId"]),
        CaseId = L(r["CaseId"]),
        VersionNo = I(r["VersionNo"]),
        Status = S(r["Status"]),
        GenerationMode = S(r["GenerationMode"]),
        RevisionReason = SN(r["RevisionReason"]),
        RevisionNotes = SN(r["RevisionNotes"]),
        SupersedesQuoteVersionId = LN(r["SupersedesQuoteVersionId"]) is { } sv ? (int?)checked((int)sv) : null,
        GrossPremium = D(r["PremiumBeforeGst"]),
        CoveredLives = I(r["CoveredLives"]),
        InputsHash = SN(r["InputsHash"]),
        Pricing = Jd<PricingResult>(SN(r["PricingJson"])),
        Analytics = Jd<AnalyticsSnapshot>(SN(r["AnalyticsJson"])),
        Guardrails = Jd<GuardrailResult>(SN(r["GuardrailsJson"])),
        BenefitDelta = Jd<List<BenefitDeltaLine>>(SN(r["BenefitDeltaJson"])) ?? new(),
        CreatedAtUtc = T(r["CreatedAtUtc"]),
        CreatedBy = S(r["CreatedBy"]),
    };

    private const string QuoteSelect = """
        SELECT QuoteVersionId, CaseId, VersionNo, Status, GenerationMode, RevisionReason, RevisionNotes,
               SupersedesQuoteVersionId, PremiumBeforeGst, CoveredLives, InputsHash, PricingJson, AnalyticsJson,
               GuardrailsJson, BenefitDeltaJson, CreatedAtUtc, CreatedBy
          FROM uw.QuoteVersion
        """;

    public async Task<List<QuoteVersion>> GetQuoteVersionsAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, QuoteSelect + " WHERE CaseId = @case ORDER BY VersionNo");
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<QuoteVersion>();
        while (await r.ReadAsync()) list.Add(MapQuoteVersion(r));
        return list;
    }

    public async Task<QuoteVersion?> GetCurrentQuoteVersionAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, QuoteSelect + " WHERE CaseId = @case AND Status = N'Current'");
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapQuoteVersion(r) : null;
    }

    public async Task SupersedeQuoteVersionAsync(long caseId, int newVersionId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            UPDATE uw.QuoteVersion SET Status = N'Superseded', SupersededByQuoteVersionId = @new, UpdatedAtUtc = SYSUTCDATETIME()
             WHERE CaseId = @case AND Status = N'Current' AND QuoteVersionId <> @new
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@new", newVersionId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────── underwriting actions ───────────────────────

    public async Task AddUnderwritingActionAsync(UnderwritingAction a)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT uw.UnderwritingAction (CaseId, QuoteVersionId, ActionType, Actor, AuthorityCheck, Remarks, PerformedAtUtc)
            VALUES (@case, @qvid, @action, @actor, @check, @remarks, @at)
            """);
        cmd.Parameters.AddWithValue("@case", a.CaseId);
        cmd.Parameters.AddWithValue("@qvid", a.QuoteVersionId);
        cmd.Parameters.AddWithValue("@action", a.Action);
        cmd.Parameters.AddWithValue("@actor", a.Actor);
        cmd.Parameters.AddWithValue("@check", a.AuthorityCheck);
        cmd.Parameters.AddWithValue("@remarks", (object?)a.Remarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", a.AtUtc);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── ingestion ───────────────────────────

    public async Task<List<RawRow>> AddRawRowsAsync(long documentId, string sheetName, List<string[]> rows)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        var result = new List<RawRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            var payload = J(cells);
            var rowHash = Hash(payload);
            using var cmd = Cmd(conn, """
                INSERT ing.RawRow (DocumentId, RowNumber, SheetName, PayloadJson, RowHash)
                OUTPUT INSERTED.RawRowId
                VALUES (@doc, @row, @sheet, @payload, @hash)
                """);
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@doc", documentId);
            cmd.Parameters.AddWithValue("@row", i + 1);
            cmd.Parameters.AddWithValue("@sheet", sheetName);
            cmd.Parameters.AddWithValue("@payload", payload);
            cmd.Parameters.AddWithValue("@hash", rowHash);
            result.Add(new RawRow
            {
                RawRowId = (long)(await cmd.ExecuteScalarAsync())!,
                DocumentId = documentId,
                RowNumber = i + 1,
                SheetName = sheetName,
                Cells = cells,
                RowHash = rowHash,
            });
        }
        await tx.CommitAsync();
        return result;
    }

    public async Task DeleteRawRowsAsync(long documentId, string sheetName)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "DELETE FROM ing.RawRow WHERE DocumentId = @doc AND SheetName = @sheet");
        cmd.Parameters.AddWithValue("@doc", documentId);
        cmd.Parameters.AddWithValue("@sheet", sheetName);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SI scenario (prototype v74 step 5) ────────────────────────────────

    public async Task<List<StdClaim>> GetStdClaimsAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT s.StdClaimId, s.CaseId, s.ClaimNumber, s.MemberNumber, s.ClaimStatus, s.PaidAmount,
                   s.DiseaseCategory, s.DiagnosisText, s.IsLargeClaim, s.AdmissionDate,
                   CAST(COALESCE(m.HasChronic, 0) AS BIT) AS IsChronic
              FROM ing.StdClaim s
              LEFT JOIN ing.StdMember m ON m.CaseId = s.CaseId AND m.MemberNumber = s.MemberNumber
             WHERE s.CaseId = @case ORDER BY s.StdClaimId
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<StdClaim>();
        while (await r.ReadAsync())
            list.Add(new StdClaim
            {
                StdClaimId = L(r["StdClaimId"]),
                CaseId = L(r["CaseId"]),
                ClaimNumber = S(r["ClaimNumber"]),
                MemberNumber = S(r["MemberNumber"]),
                ClaimStatus = S(r["ClaimStatus"]),
                PaidAmount = D(r["PaidAmount"]),
                DiseaseCategory = S(r["DiagnosisText"]) is { Length: > 0 } d ? d : null,
                DiagnosisText = S(r["DiagnosisText"]) is { Length: > 0 } dt ? dt : null,
                IsLargeClaim = B(r["IsLargeClaim"]),
                IsChronic = B(r["IsChronic"]),
                AdmissionDate = r["AdmissionDate"] is DateTime dt2 ? dt2 : null,
            });
        return list;
    }

    public async Task SaveStdClaimsAsync(long caseId, IReadOnlyList<StdClaim> claims)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        using (var del = Cmd(conn, "DELETE FROM ing.StdClaim WHERE CaseId = @case"))
        { del.Transaction = tx; del.Parameters.AddWithValue("@case", caseId); await del.ExecuteNonQueryAsync(); }
        foreach (var cl in claims)
        {
            using var cmd = Cmd(conn, """
                INSERT ing.StdClaim (CaseId, ClaimNumber, MemberNumber, ClaimStatus, PaidAmount,
                                     DiseaseCategory, DiagnosisText, IsLargeClaim, AdmissionDate)
                VALUES (@case, @cn, @mn, @st, @paid, @dc, @dx, @large, @adm)
                """);
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@case", caseId);
            cmd.Parameters.AddWithValue("@cn", cl.ClaimNumber);
            cmd.Parameters.AddWithValue("@mn", cl.MemberNumber ?? "");
            cmd.Parameters.AddWithValue("@st", cl.ClaimStatus);
            cmd.Parameters.AddWithValue("@paid", cl.PaidAmount);
            cmd.Parameters.AddWithValue("@dc", (object?)cl.DiseaseCategory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dx", (object?)cl.DiagnosisText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@large", cl.IsLargeClaim);
            cmd.Parameters.AddWithValue("@adm", (object?)cl.AdmissionDate ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<int> GetStdMemberCountAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT COALESCE(
                NULLIF((SELECT COUNT(*) FROM ing.StdMember WHERE CaseId = @case), 0),
                (SELECT TOP 1 CoveredLives FROM uw.AnalyticsSnapshot WHERE CaseId = @case ORDER BY AnalyticsSnapshotId DESC), 0)
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        var r = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(r);
    }

    public async Task<Dictionary<string, decimal>> GetFormulaConfigAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            IF OBJECT_ID('uw.FormulaConfig') IS NULL
                CREATE TABLE uw.FormulaConfig (ConfigKey NVARCHAR(64) NOT NULL PRIMARY KEY, ConfigValue DECIMAL(18,4) NOT NULL);
            SELECT ConfigKey, ConfigValue FROM uw.FormulaConfig
            """);
        using var r = await cmd.ExecuteReaderAsync();
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["TrendPct"] = 8.0m,   // engine defaults when unset
            ["IbnrPct"] = 5.0m,
        };
        while (await r.ReadAsync())
            dict[S(r["ConfigKey"])] = D(r["ConfigValue"]);
        return dict;
    }

    public async Task SaveFormulaConfigAsync(Dictionary<string, decimal> values)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            IF OBJECT_ID('uw.FormulaConfig') IS NULL
                CREATE TABLE uw.FormulaConfig (ConfigKey NVARCHAR(64) NOT NULL PRIMARY KEY, ConfigValue DECIMAL(18,4) NOT NULL);
            """);
        await cmd.ExecuteNonQueryAsync();
        foreach (var kv in values)
        {
            using var up = Cmd(conn, """
                MERGE uw.FormulaConfig AS t
                USING (SELECT @k AS ConfigKey, @v AS ConfigValue) AS s
                ON t.ConfigKey = s.ConfigKey
                WHEN MATCHED THEN UPDATE SET ConfigValue = s.ConfigValue
                WHEN NOT MATCHED THEN INSERT (ConfigKey, ConfigValue) VALUES (s.ConfigKey, s.ConfigValue);
                """);
            up.Parameters.AddWithValue("@k", kv.Key);
            up.Parameters.AddWithValue("@v", kv.Value);
            await up.ExecuteNonQueryAsync();
        }
    }

    public async Task<bool> HasRawRowsAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM ing.RawRow rr
                JOIN core.CaseDocument d ON d.DocumentId = rr.DocumentId
                 WHERE d.CaseId = @case) THEN 1 ELSE 0 END
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        var r = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(r) == 1;
    }

    public async Task<SIScenarioRow?> GetSIScenarioAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT CaseId, Factor3To4, Factor3To5, Factor4To5, ApplyToPricing, Inc3To4, Inc3To5, Inc4To5, IncrementalPerLife, UpdatedBy, UpdatedAtUtc FROM uw.SIScenario WHERE CaseId = @case");
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new SIScenarioRow
        {
            CaseId = L(r["CaseId"]),
            Factor3To4 = D(r["Factor3To4"]),
            Factor3To5 = D(r["Factor3To5"]),
            Factor4To5 = D(r["Factor4To5"]),
            ApplyToPricing = B(r["ApplyToPricing"]),
            Inc3To4 = D(r["Inc3To4"]),
            Inc3To5 = D(r["Inc3To5"]),
            Inc4To5 = D(r["Inc4To5"]),
            IncrementalPerLife = D(r["IncrementalPerLife"]),
            UpdatedBy = S(r["UpdatedBy"]) is { Length: > 0 } u ? u : null,
            UpdatedAtUtc = T(r["UpdatedAtUtc"]),
        };
    }

    public async Task SaveSIScenarioAsync(SIScenarioRow row)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            MERGE uw.SIScenario AS t
            USING (SELECT @case AS CaseId) AS s ON t.CaseId = s.CaseId
            WHEN MATCHED THEN UPDATE SET Factor3To4=@f34, Factor3To5=@f35, Factor4To5=@f45,
                ApplyToPricing=@apply, UpdatedBy=@by, UpdatedAtUtc=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (CaseId, Factor3To4, Factor3To5, Factor4To5, ApplyToPricing, UpdatedBy)
                VALUES (@case, @f34, @f35, @f45, @apply, @by);
            """);
        cmd.Parameters.AddWithValue("@case", row.CaseId);
        cmd.Parameters.AddWithValue("@f34", row.Factor3To4);
        cmd.Parameters.AddWithValue("@f35", row.Factor3To5);
        cmd.Parameters.AddWithValue("@f45", row.Factor4To5);
        cmd.Parameters.AddWithValue("@apply", row.ApplyToPricing);
        cmd.Parameters.AddWithValue("@by", (object?)row.UpdatedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<PricingMasterVersion?> GetActivePricingMasterAsync()
    {
        var m = await GetActiveMasterVersionAsync();
        return m;
    }

    // ── commercial discount loop (prototype v74 step 8) ───────────────────

    public async Task<DiscountRequestRow> SaveDiscountRequestAsync(DiscountRequestRow row)
    {
        await using var conn = await OpenAsync();
        if (row.DiscountRequestId == 0)
        {
            using var cmd = Cmd(conn, """
                INSERT uw.DiscountRequest (CaseId, QuoteVersionId, RequestedByUserId, RequestedPct,
                     RequestedRemarks, JustificationType, CompetitorQuoteId, Status, RequestedAtUtc)
                OUTPUT INSERTED.DiscountRequestId
                VALUES (@case, @qv, @by, @pct, @rem, @jt, @cq, @st, SYSUTCDATETIME())
                """);
            cmd.Parameters.AddWithValue("@case", row.CaseId);
            cmd.Parameters.AddWithValue("@qv", row.QuoteVersionId);
            cmd.Parameters.AddWithValue("@by", row.RequestedByUserId);
            cmd.Parameters.AddWithValue("@pct", row.RequestedPct);
            cmd.Parameters.AddWithValue("@rem", (object?)row.RequestedRemarks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@jt", (object?)row.JustificationType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cq", (object?)row.CompetitorQuoteId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@st", row.Status);
            row.DiscountRequestId = L(await cmd.ExecuteScalarAsync());
        }
        else
        {
            using var cmd = Cmd(conn, """
                UPDATE uw.DiscountRequest SET Status=@st, ApprovedPct=@ap, ApprovedByUserId=@aby,
                     ApprovalRemarks=@arem, ResultingQuoteVersionId=@rqv, DecidedAtUtc=SYSUTCDATETIME()
                WHERE DiscountRequestId=@id
                """);
            cmd.Parameters.AddWithValue("@id", row.DiscountRequestId);
            cmd.Parameters.AddWithValue("@st", row.Status);
            cmd.Parameters.AddWithValue("@ap", (object?)row.ApprovedPct ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@aby", (object?)row.ApprovedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@arem", (object?)row.ApprovalRemarks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rqv", (object?)row.ResultingQuoteVersionId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        return row;
    }

    public async Task<List<DiscountRequestRow>> GetDiscountRequestsAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT d.DiscountRequestId, d.CaseId, d.QuoteVersionId, d.RequestedByUserId, d.RequestedPct,
                   d.RequestedRemarks, d.JustificationType, d.CompetitorQuoteId, d.Status, d.ApprovedPct,
                   d.ApprovedByUserId, d.ApprovalRemarks, d.ResultingQuoteVersionId, d.RequestedAtUtc, d.DecidedAtUtc,
                   ru.Email AS RequestedByEmail, au.Email AS ApprovedByEmail
              FROM uw.DiscountRequest d
              LEFT JOIN ref.[User] ru ON ru.UserId = d.RequestedByUserId
              LEFT JOIN ref.[User] au ON au.UserId = d.ApprovedByUserId
             WHERE d.CaseId = @case ORDER BY d.DiscountRequestId
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        return await ReadDiscountRequestsAsync(r);
    }

    public async Task<DiscountRequestRow?> GetDiscountRequestAsync(long id)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT d.DiscountRequestId, d.CaseId, d.QuoteVersionId, d.RequestedByUserId, d.RequestedPct,
                   d.RequestedRemarks, d.JustificationType, d.CompetitorQuoteId, d.Status, d.ApprovedPct,
                   d.ApprovedByUserId, d.ApprovalRemarks, d.ResultingQuoteVersionId, d.RequestedAtUtc, d.DecidedAtUtc,
                   ru.Email AS RequestedByEmail, au.Email AS ApprovedByEmail
              FROM uw.DiscountRequest d
              LEFT JOIN ref.[User] ru ON ru.UserId = d.RequestedByUserId
              LEFT JOIN ref.[User] au ON au.UserId = d.ApprovedByUserId
             WHERE d.DiscountRequestId = @id
            """);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();
        return (await ReadDiscountRequestsAsync(r)).FirstOrDefault();
    }

    private static async Task<List<DiscountRequestRow>> ReadDiscountRequestsAsync(SqlDataReader r)
    {
        var list = new List<DiscountRequestRow>();
        while (await r.ReadAsync())
            list.Add(new DiscountRequestRow
            {
                DiscountRequestId = L(r["DiscountRequestId"]),
                CaseId = L(r["CaseId"]),
                QuoteVersionId = L(r["QuoteVersionId"]),
                RequestedByUserId = (int)I(r["RequestedByUserId"]),
                RequestedByEmail = S(r["RequestedByEmail"]) ?? "",
                RequestedPct = D(r["RequestedPct"]),
                RequestedRemarks = S(r["RequestedRemarks"]) is { Length: > 0 } rm ? rm : null,
                JustificationType = S(r["JustificationType"]) is { Length: > 0 } jt ? jt : null,
                CompetitorQuoteId = r["CompetitorQuoteId"] is DBNull ? null : L(r["CompetitorQuoteId"]),
                Status = S(r["Status"]),
                ApprovedPct = r["ApprovedPct"] is DBNull ? null : D(r["ApprovedPct"]),
                ApprovedByUserId = r["ApprovedByUserId"] is DBNull ? null : (int)I(r["ApprovedByUserId"]),
                ApprovedByEmail = S(r["ApprovedByEmail"]) is { Length: > 0 } ae ? ae : null,
                ApprovalRemarks = S(r["ApprovalRemarks"]) is { Length: > 0 } ar ? ar : null,
                ResultingQuoteVersionId = r["ResultingQuoteVersionId"] is DBNull ? null : L(r["ResultingQuoteVersionId"]),
                RequestedAtUtc = T(r["RequestedAtUtc"]),
                DecidedAtUtc = r["DecidedAtUtc"] is DBNull ? null : T(r["DecidedAtUtc"]),
            });
        return list;
    }

    // ── analytics persistence (prototype §5) ──────────────────────────────

    public async Task<List<(string ChannelType, decimal DefaultRatePct, decimal MinRatePct, decimal MaxRatePct)>> GetCommissionScheduleAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT ChannelType, DefaultRatePct, MinRatePct, MaxRatePct FROM uw.CommissionMaster WHERE IsActive = 1 ORDER BY CommissionMasterId");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(string, decimal, decimal, decimal)>();
        while (await r.ReadAsync())
            list.Add((S(r["ChannelType"]) ?? "", D(r["DefaultRatePct"]), D(r["MinRatePct"]), D(r["MaxRatePct"])));
        return list;
    }

    // ── Excel masters upload (prototype §7) ───────────────────────────────

    public async Task UpsertBenefitCostingAsync(BenefitCostingRow m)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            ;IF EXISTS (SELECT 1 FROM uw.BenefitCostingMaster WHERE PricingMasterVersionId = @mv AND BenefitCode = @code)
                UPDATE uw.BenefitCostingMaster SET UtilizationRate=@util, SeverityCaptureRate=@sev, IncidenceRate=@inc,
                     SeverityAmount=@amt, FactorPct=@fac, ExperienceCredibilityPct=@cred, AttributionFactor=@attr
                 WHERE PricingMasterVersionId = @mv AND BenefitCode = @code
            ELSE
                INSERT uw.BenefitCostingMaster (PricingMasterVersionId, BenefitCode, UtilizationRate, SeverityCaptureRate,
                     IncidenceRate, SeverityAmount, FactorPct, ExperienceCredibilityPct, AttributionFactor)
                VALUES (@mv, @code, @util, @sev, @inc, @amt, @fac, @cred, @attr);
            """);
        cmd.Parameters.AddWithValue("@mv", m.MasterVersionId);
        cmd.Parameters.AddWithValue("@code", m.BenefitCode);
        cmd.Parameters.AddWithValue("@util", m.UtilizationRate);
        cmd.Parameters.AddWithValue("@sev", m.SeverityCaptureRate);
        cmd.Parameters.AddWithValue("@inc", m.IncidenceRate);
        cmd.Parameters.AddWithValue("@amt", m.SeverityAmount);
        cmd.Parameters.AddWithValue("@fac", m.FactorPct);
        cmd.Parameters.AddWithValue("@cred", m.ExperienceCredibilityPct);
        cmd.Parameters.AddWithValue("@attr", m.AttributionFactor);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertCommissionAsync(CommissionRow m)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            ;IF EXISTS (SELECT 1 FROM uw.CommissionMaster WHERE PricingMasterVersionId = @mv AND ChannelType = @ch)
                UPDATE uw.CommissionMaster SET DefaultRatePct=@def, MinRatePct=@min, MaxRatePct=@max, IsActive=@act
                 WHERE PricingMasterVersionId = @mv AND ChannelType = @ch
            ELSE
                INSERT uw.CommissionMaster (PricingMasterVersionId, ChannelType, DefaultRatePct, MinRatePct, MaxRatePct, IsActive)
                VALUES (@mv, @ch, @def, @min, @max, @act);
            """);
        cmd.Parameters.AddWithValue("@mv", m.MasterVersionId);
        cmd.Parameters.AddWithValue("@ch", m.ChannelType);
        cmd.Parameters.AddWithValue("@def", m.DefaultRatePct);
        cmd.Parameters.AddWithValue("@min", m.MinRatePct);
        cmd.Parameters.AddWithValue("@max", m.MaxRatePct);
        cmd.Parameters.AddWithValue("@act", m.IsActive);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertStandardLoadingAsync(StandardLoadingRow m)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            ;IF EXISTS (SELECT 1 FROM uw.StandardLoadingMaster WHERE PricingMasterVersionId = @mv AND LoadingName = @nm)
                UPDATE uw.StandardLoadingMaster SET Value = @val WHERE PricingMasterVersionId = @mv AND LoadingName = @nm
            ELSE
                INSERT uw.StandardLoadingMaster (PricingMasterVersionId, LoadingName, Value) VALUES (@mv, @nm, @val);
            """);
        cmd.Parameters.AddWithValue("@mv", m.MasterVersionId);
        cmd.Parameters.AddWithValue("@nm", m.LoadingName);
        cmd.Parameters.AddWithValue("@val", m.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<BenefitCostingRow>> GetBenefitCostingAsync(int masterVersionId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT BenefitCostingMasterId, PricingMasterVersionId, BenefitCode, UtilizationRate, SeverityCaptureRate, IncidenceRate, SeverityAmount, FactorPct, ExperienceCredibilityPct, AttributionFactor FROM uw.BenefitCostingMaster WHERE PricingMasterVersionId = @mv ORDER BY BenefitCode");
        cmd.Parameters.AddWithValue("@mv", masterVersionId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BenefitCostingRow>();
        while (await r.ReadAsync())
            list.Add(new BenefitCostingRow
            {
                BenefitCostingMasterId = L(r["BenefitCostingMasterId"]),
                MasterVersionId = I(r["PricingMasterVersionId"]),
                BenefitCode = S(r["BenefitCode"]) ?? "",
                UtilizationRate = D(r["UtilizationRate"]),
                SeverityCaptureRate = D(r["SeverityCaptureRate"]),
                IncidenceRate = D(r["IncidenceRate"]),
                SeverityAmount = D(r["SeverityAmount"]),
                FactorPct = D(r["FactorPct"]),
                ExperienceCredibilityPct = D(r["ExperienceCredibilityPct"]),
                AttributionFactor = D(r["AttributionFactor"]),
            });
        return list;
    }

    public async Task<List<CommissionRow>> GetCommissionMasterAsync(int masterVersionId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT CommissionMasterId, PricingMasterVersionId, ChannelType, DefaultRatePct, MinRatePct, MaxRatePct, IsActive FROM uw.CommissionMaster WHERE PricingMasterVersionId = @mv ORDER BY CommissionMasterId");
        cmd.Parameters.AddWithValue("@mv", masterVersionId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CommissionRow>();
        while (await r.ReadAsync())
            list.Add(new CommissionRow
            {
                CommissionMasterId = L(r["CommissionMasterId"]),
                MasterVersionId = I(r["PricingMasterVersionId"]),
                ChannelType = S(r["ChannelType"]) ?? "",
                DefaultRatePct = D(r["DefaultRatePct"]),
                MinRatePct = D(r["MinRatePct"]),
                MaxRatePct = D(r["MaxRatePct"]),
                IsActive = B(r["IsActive"]),
            });
        return list;
    }

    public async Task<List<StandardLoadingRow>> GetStandardLoadingAsync(int masterVersionId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT StandardLoadingMasterId, PricingMasterVersionId, LoadingName, Value FROM uw.StandardLoadingMaster WHERE PricingMasterVersionId = @mv ORDER BY StandardLoadingMasterId");
        cmd.Parameters.AddWithValue("@mv", masterVersionId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<StandardLoadingRow>();
        while (await r.ReadAsync())
            list.Add(new StandardLoadingRow
            {
                StandardLoadingMasterId = L(r["StandardLoadingMasterId"]),
                MasterVersionId = I(r["PricingMasterVersionId"]),
                LoadingName = S(r["LoadingName"]) ?? "",
                Value = D(r["Value"]),
            });
        return list;
    }

    public async Task<long> SaveAnalyticsSnapshotAsync(long caseId, AnalyticsSnapshot snap, long? stageRunId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT uw.AnalyticsSnapshot (CaseId, CaseStageRunId, SnapshotVersion, IsCurrent,
                CoveredLives, EmployeeCount, ClaimCount, PaidClaimCount, RejectedClaimCount,
                TotalClaimedAmount, TotalApprovedAmount, TotalSanctionedAmount, TotalPaidAmount,
                TotalOutstandingAmount, TotalIncurredAmount, AverageClaimAmount, MedianClaimAmount,
                ClaimFrequencyPer100, ClaimantIncidencePct, ClaimCostPerLife, BurnCostPerLife,
                LargeClaimCostSharePct, Top5ClaimSharePct, SingleLargestClaimSharePct,
                ClaimVolatilityCv, ChronicPrevalencePct, MaternitySharePct, NonNetworkCostSharePct,
                ParentSharePct, Age55PlusSharePct, AverageAge, TrendNarrative, MetricsJson, CreatedAtUtc)
            OUTPUT INSERTED.AnalyticsSnapshotId
            VALUES (@case, @run, 1, 1, @lives, @emps, @claims, @paid, @rej, @claimed, @approved, @sanc,
                @paidAmt, @outst, @incurred, @avgClaim, @medClaim, @freq, @incid, @cpl, @burn,
                @large, @top5, @single, @cv, @chronic, @mat, @nonet, @parent, @senior, @avgAge,
                @narr, @metrics, SYSUTCDATETIME())
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@run", (object?)stageRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lives", snap.CoveredLives);
        cmd.Parameters.AddWithValue("@emps", snap.EmployeeCount);
        cmd.Parameters.AddWithValue("@claims", snap.ClaimCount);
        cmd.Parameters.AddWithValue("@paid", snap.PaidClaimCount);
        cmd.Parameters.AddWithValue("@rej", snap.RejectedClaimCount);
        cmd.Parameters.AddWithValue("@claimed", snap.AmountClaimed);
        cmd.Parameters.AddWithValue("@approved", snap.AmountApproved);
        cmd.Parameters.AddWithValue("@sanc", snap.AmountSanctioned);
        cmd.Parameters.AddWithValue("@paidAmt", snap.AmountPaid);
        cmd.Parameters.AddWithValue("@outst", snap.AmountOutstanding);
        cmd.Parameters.AddWithValue("@incurred", snap.AmountIncurred);
        cmd.Parameters.AddWithValue("@avgClaim", snap.AvgClaimSize);
        cmd.Parameters.AddWithValue("@medClaim", snap.MedianClaimSize);
        cmd.Parameters.AddWithValue("@freq", snap.FrequencyPer100);
        cmd.Parameters.AddWithValue("@incid", snap.ClaimantIncidencePct);
        cmd.Parameters.AddWithValue("@cpl", snap.CostPerLife);
        cmd.Parameters.AddWithValue("@burn", snap.BurnCostPerLife);
        cmd.Parameters.AddWithValue("@large", snap.LargeClaimSharePct);
        cmd.Parameters.AddWithValue("@top5", snap.Top5ClaimSharePct);
        cmd.Parameters.AddWithValue("@single", snap.SingleLargestSharePct);
        cmd.Parameters.AddWithValue("@cv", snap.VolatilityCv);
        cmd.Parameters.AddWithValue("@chronic", snap.ChronicPrevalencePct);
        cmd.Parameters.AddWithValue("@mat", snap.MaternitySharePct);
        cmd.Parameters.AddWithValue("@nonet", snap.NonNetworkSharePct);
        cmd.Parameters.AddWithValue("@parent", snap.ParentSharePct);
        cmd.Parameters.AddWithValue("@senior", snap.SeniorAgeSharePct);
        cmd.Parameters.AddWithValue("@avgAge", snap.AvgAge);
        cmd.Parameters.AddWithValue("@narr", (object?)snap.TrendNarrative ?? DBNull.Value);
        var metrics = new Dictionary<string, decimal>();
        foreach (var (k, v) in snap.ByAgeBand) metrics[$"age:{k}"] = v;
        foreach (var (k, v) in snap.ByRelationship) metrics[$"rel:{k}"] = v;
        foreach (var (k, v) in snap.ByDiseaseGroup) metrics[$"dis:{k}"] = v;
        cmd.Parameters.AddWithValue("@metrics", JsonSerializer.Serialize(metrics));
        return L(await cmd.ExecuteScalarAsync());
    }

    public async Task SaveAnalyticsBreakdownsAsync(long snapshotId, IReadOnlyList<AnalyticsBreakdownRow> rows)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        using (var del = Cmd(conn, "DELETE FROM uw.AnalyticsBreakdown WHERE AnalyticsSnapshotId = @sid"))
        { del.Transaction = tx; del.Parameters.AddWithValue("@sid", snapshotId); await del.ExecuteNonQueryAsync(); }
        foreach (var r in rows)
        {
            using var cmd = Cmd(conn, """
                INSERT uw.AnalyticsBreakdown (AnalyticsSnapshotId, Dimension, DimensionValue, Lives,
                     ClaimCount, PaidAmount, IncurredAmount, FrequencyPer100, CostSharePct, AverageClaimAmount)
                VALUES (@sid, @dim, @val, @lives, @cnt, @paid, @inc, @freq, @share, @avg)
                """);
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@sid", snapshotId);
            cmd.Parameters.AddWithValue("@dim", r.Dimension);
            cmd.Parameters.AddWithValue("@val", r.DimensionValue);
            cmd.Parameters.AddWithValue("@lives", r.Lives);
            cmd.Parameters.AddWithValue("@cnt", r.ClaimCount);
            cmd.Parameters.AddWithValue("@paid", r.PaidAmount);
            cmd.Parameters.AddWithValue("@inc", r.IncurredAmount);
            cmd.Parameters.AddWithValue("@freq", r.FrequencyPer100);
            cmd.Parameters.AddWithValue("@share", r.CostSharePct);
            cmd.Parameters.AddWithValue("@avg", r.AverageClaimAmount);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<AnalyticsSnapshot?> GetAnalyticsSnapshotAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT TOP 1 AnalyticsSnapshotId, CoveredLives, EmployeeCount, ClaimCount, PaidClaimCount,
                   RejectedClaimCount, TotalClaimedAmount, TotalIncurredAmount, TotalPaidAmount,
                   TotalOutstandingAmount, AverageClaimAmount, MedianClaimAmount, ClaimFrequencyPer100,
                   ClaimantIncidencePct, ClaimCostPerLife, BurnCostPerLife, LargeClaimCostSharePct,
                   Top5ClaimSharePct, SingleLargestClaimSharePct, ClaimVolatilityCv, ChronicPrevalencePct,
                   MaternitySharePct, NonNetworkCostSharePct, ParentSharePct, Age55PlusSharePct,
                   AverageAge, TrendNarrative, MetricsJson
              FROM uw.AnalyticsSnapshot WHERE CaseId = @case AND IsCurrent = 1
             ORDER BY AnalyticsSnapshotId DESC
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var snap = new AnalyticsSnapshot
        {
            CoveredLives = I(r["CoveredLives"]),
            EmployeeCount = I(r["EmployeeCount"]),
            ClaimCount = I(r["ClaimCount"]),
            PaidClaimCount = I(r["PaidClaimCount"]),
            RejectedClaimCount = I(r["RejectedClaimCount"]),
            AmountClaimed = D(r["TotalClaimedAmount"]),
            AmountIncurred = D(r["TotalIncurredAmount"]),
            AmountPaid = D(r["TotalPaidAmount"]),
            AmountOutstanding = D(r["TotalOutstandingAmount"]),
            AvgClaimSize = D(r["AverageClaimAmount"]),
            MedianClaimSize = D(r["MedianClaimAmount"]),
            FrequencyPer100 = D(r["ClaimFrequencyPer100"]),
            ClaimantIncidencePct = D(r["ClaimantIncidencePct"]),
            CostPerLife = D(r["ClaimCostPerLife"]),
            BurnCostPerLife = D(r["BurnCostPerLife"]),
            LargeClaimSharePct = D(r["LargeClaimCostSharePct"]),
            Top5ClaimSharePct = D(r["Top5ClaimSharePct"]),
            SingleLargestSharePct = D(r["SingleLargestClaimSharePct"]),
            VolatilityCv = D(r["ClaimVolatilityCv"]),
            ChronicPrevalencePct = D(r["ChronicPrevalencePct"]),
            MaternitySharePct = D(r["MaternitySharePct"]),
            NonNetworkSharePct = D(r["NonNetworkCostSharePct"]),
            ParentSharePct = D(r["ParentSharePct"]),
            SeniorAgeSharePct = D(r["Age55PlusSharePct"]),
            AvgAge = D(r["AverageAge"]),
            TrendNarrative = S(r["TrendNarrative"]) is { Length: > 0 } tn ? tn : null,
        };
        var metrics = S(r["MetricsJson"]);
        if (metrics is { Length: > 2 })
        {
            try
            {
                using var doc = JsonDocument.Parse(metrics);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    var v = p.Value.GetDecimal();
                    if (p.Name.StartsWith("age:", StringComparison.Ordinal)) snap.ByAgeBand[p.Name[4..]] = v;
                    else if (p.Name.StartsWith("rel:", StringComparison.Ordinal)) snap.ByRelationship[p.Name[4..]] = v;
                    else if (p.Name.StartsWith("dis:", StringComparison.Ordinal)) snap.ByDiseaseGroup[p.Name[4..]] = v;
                }
            }
            catch { /* metrics optional */ }
        }
        return snap;
    }

    public async Task<List<AnalyticsBreakdownRow>> GetAnalyticsBreakdownsAsync(long snapshotId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT AnalyticsBreakdownId, AnalyticsSnapshotId, Dimension, DimensionValue, Lives,
                   ClaimCount, PaidAmount, IncurredAmount, FrequencyPer100, CostSharePct, AverageClaimAmount
              FROM uw.AnalyticsBreakdown WHERE AnalyticsSnapshotId = @sid ORDER BY AnalyticsBreakdownId
            """);
        cmd.Parameters.AddWithValue("@sid", snapshotId);
        using var r = await cmd.ExecuteReaderAsync();
        return await ReadBreakdownsAsync(r);
    }

    public async Task<AnalyticsBreakdownRow?> GetAnalyticsBreakdownForCaseAsync(long caseId, string dimension)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT TOP 1 b.AnalyticsBreakdownId, b.AnalyticsSnapshotId, b.Dimension, b.DimensionValue, b.Lives,
                   b.ClaimCount, b.PaidAmount, b.IncurredAmount, b.FrequencyPer100, b.CostSharePct, b.AverageClaimAmount
              FROM uw.AnalyticsBreakdown b
              JOIN uw.AnalyticsSnapshot s ON s.AnalyticsSnapshotId = b.AnalyticsSnapshotId
             WHERE s.CaseId = @case AND s.IsCurrent = 1 AND b.Dimension = @dim
             ORDER BY b.CostSharePct DESC
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@dim", dimension);
        using var r = await cmd.ExecuteReaderAsync();
        return (await ReadBreakdownsAsync(r)).FirstOrDefault();
    }

    private static async Task<List<AnalyticsBreakdownRow>> ReadBreakdownsAsync(SqlDataReader r)
    {
        var list = new List<AnalyticsBreakdownRow>();
        while (await r.ReadAsync())
            list.Add(new AnalyticsBreakdownRow
            {
                AnalyticsBreakdownId = L(r["AnalyticsBreakdownId"]),
                AnalyticsSnapshotId = L(r["AnalyticsSnapshotId"]),
                Dimension = S(r["Dimension"]) ?? "",
                DimensionValue = S(r["DimensionValue"]) ?? "",
                Lives = I(r["Lives"]),
                ClaimCount = I(r["ClaimCount"]),
                PaidAmount = D(r["PaidAmount"]),
                IncurredAmount = D(r["IncurredAmount"]),
                FrequencyPer100 = D(r["FrequencyPer100"]),
                CostSharePct = D(r["CostSharePct"]),
                AverageClaimAmount = D(r["AverageClaimAmount"]),
            });
        return list;
    }

    public async Task<List<CompetitorQuoteRow>> GetCompetitorQuotesForCaseAsync(long caseId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT CompetitorQuoteId, CaseId, CompetitorName, CoveredLives, RatePerLife,
                   PremiumBeforeGst, PremiumInclGst, ExtractionConfidence
              FROM uw.CompetitorQuote WHERE CaseId = @case ORDER BY CompetitorQuoteId
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CompetitorQuoteRow>();
        while (await r.ReadAsync())
            list.Add(new CompetitorQuoteRow
            {
                CompetitorQuoteId = L(r["CompetitorQuoteId"]),
                CaseId = L(r["CaseId"]),
                Name = S(r["CompetitorName"]) ?? "",
                CoveredLives = I(r["CoveredLives"]),
                SumInsured = 0m,
                Premium = D(r["PremiumInclGst"]),
                RatePerLife = D(r["RatePerLife"]),
                ExtractionConfidence = D(r["ExtractionConfidence"]),
            });
        return list;
    }

    public async Task<List<RawRow>> GetRawRowsAsync(long documentId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT RawRowId, DocumentId, RowNumber, SheetName, PayloadJson, RowHash FROM ing.RawRow WHERE DocumentId = @doc ORDER BY RowNumber");
        cmd.Parameters.AddWithValue("@doc", documentId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<RawRow>();
        while (await r.ReadAsync())
            list.Add(new RawRow
            {
                RawRowId = L(r["RawRowId"]),
                DocumentId = L(r["DocumentId"]),
                RowNumber = I(r["RowNumber"]),
                SheetName = S(r["SheetName"]),
                Cells = Jd<string[]>(S(r["PayloadJson"])) ?? Array.Empty<string>(),
                RowHash = S(r["RowHash"]),
            });
        return list;
    }

    public async Task<SourceColumn> AddOrUpdateSourceColumnAsync(SourceColumn col)
    {
        await using var conn = await OpenAsync();
        if (col.SourceColumnId == 0)
        {
            using var cmd = Cmd(conn, """
                INSERT ing.SourceColumn (DocumentId, OrdinalPosition, SourceHeader, NormalizedHeader, DetectedDataType,
                                         NonEmptyCount, DistinctCount, SampleValuesJson, TargetParameter, Confidence,
                                         Provenance, Reason, IsRequired)
                OUTPUT INSERTED.SourceColumnId
                VALUES (@doc, @ord, @hdr, @nhdr, @type, @ne, @dc, @samples, @target, @conf, @prov, @reason, @req)
                """);
            cmd.Parameters.AddWithValue("@doc", col.DocumentId);
            cmd.Parameters.AddWithValue("@ord", col.ColumnIndex);
            cmd.Parameters.AddWithValue("@hdr", col.Header);
            cmd.Parameters.AddWithValue("@nhdr", Normalize(col.Header));
            cmd.Parameters.AddWithValue("@type", col.DetectedType);
            cmd.Parameters.AddWithValue("@ne", col.NonEmptyCount);
            cmd.Parameters.AddWithValue("@dc", col.DistinctCount);
            cmd.Parameters.AddWithValue("@samples", J(col.SampleValues));
            cmd.Parameters.AddWithValue("@target", (object?)col.TargetParameter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@conf", col.Confidence);
            cmd.Parameters.AddWithValue("@prov", (object?)col.ProvenanceTier ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@reason", (object?)col.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@req", col.IsRequired);
            col.SourceColumnId = (long)(await cmd.ExecuteScalarAsync())!;
        }
        else
        {
            using var cmd = Cmd(conn, """
                UPDATE ing.SourceColumn
                   SET TargetParameter = @target, Confidence = @conf, Provenance = @prov, Reason = @reason,
                       IsRequired = @req, UpdatedAtUtc = SYSUTCDATETIME()
                 WHERE SourceColumnId = @id
                """);
            cmd.Parameters.AddWithValue("@id", col.SourceColumnId);
            cmd.Parameters.AddWithValue("@target", (object?)col.TargetParameter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@conf", col.Confidence);
            cmd.Parameters.AddWithValue("@prov", (object?)col.ProvenanceTier ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@reason", (object?)col.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@req", col.IsRequired);
            await cmd.ExecuteNonQueryAsync();
        }
        return col;
    }

    public async Task<List<SourceColumn>> GetSourceColumnsAsync(long documentId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT SourceColumnId, DocumentId, OrdinalPosition, SourceHeader, DetectedDataType, NonEmptyCount, DistinctCount,
                   SampleValuesJson, TargetParameter, Confidence, Provenance, Reason, IsRequired
              FROM ing.SourceColumn WHERE DocumentId = @doc ORDER BY OrdinalPosition
            """);
        cmd.Parameters.AddWithValue("@doc", documentId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SourceColumn>();
        while (await r.ReadAsync())
            list.Add(new SourceColumn
            {
                SourceColumnId = L(r["SourceColumnId"]),
                DocumentId = L(r["DocumentId"]),
                ColumnIndex = I(r["OrdinalPosition"]),
                Header = S(r["SourceHeader"]),
                DetectedType = SN(r["DetectedDataType"]) ?? "Text",
                NonEmptyCount = IN(r["NonEmptyCount"]) ?? 0,
                DistinctCount = IN(r["DistinctCount"]) ?? 0,
                SampleValues = JArr(r["SampleValuesJson"]),
                TargetParameter = SN(r["TargetParameter"]),
                Confidence = DN(r["Confidence"]) ?? 0m,
                ProvenanceTier = SN(r["Provenance"]),
                Reason = SN(r["Reason"]),
                IsRequired = B(r["IsRequired"]),
            });
        return list;
    }

    public async Task AddColumnMappingAsync(ColumnMapping m)
    {
        await using var conn = await OpenAsync();
        // Append-only: a correction is a new row. Mark older rows not-current for this column.
        using var cmd = Cmd(conn, """
            ;UPDATE ing.ColumnMapping SET IsCurrent = 0 WHERE SourceColumnId = @col AND IsCurrent = 1;
            INSERT ing.ColumnMapping (SourceColumnId, MappingVersion, TargetParameter, Confidence, Provenance, Reason,
                                      IsManualOverride, AutoApproved, IsApproved, ApprovedBy, IsCurrent)
            SELECT @col, ISNULL(MAX(MappingVersion), 0) + 1, @target, @conf, @prov, @reason, @manual, @auto, @approved, @by, 1
              FROM ing.ColumnMapping WHERE SourceColumnId = @col;
            """);
        cmd.Parameters.AddWithValue("@col", m.SourceColumnId);
        cmd.Parameters.AddWithValue("@target", m.TargetParameter);
        cmd.Parameters.AddWithValue("@conf", m.Confidence);
        cmd.Parameters.AddWithValue("@prov", MapTier(m.ProvenanceTier));
        cmd.Parameters.AddWithValue("@reason", DBNull.Value);
        cmd.Parameters.AddWithValue("@manual", !m.AutoApproved);
        cmd.Parameters.AddWithValue("@auto", m.AutoApproved);
        cmd.Parameters.AddWithValue("@approved", m.AutoApproved || m.ApprovedBy is not null);
        cmd.Parameters.AddWithValue("@by", (object?)m.ApprovedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string MapTier(string? tier) => tier switch
    {
        Enums.TierExactDictionary => "Dictionary",
        Enums.TierLearnedAlias => "Learned",
        Enums.TierRetrieval => "RAG",
        Enums.TierLlmSemantic => "LLM",
        Enums.TierFuzzy => "Fuzzy",
        _ => tier ?? "Dictionary",
    };

    private static string? MapTierBack(string? p) => p switch
    {
        "Dictionary" => Enums.TierExactDictionary,
        "Learned" => Enums.TierLearnedAlias,
        "RAG" => Enums.TierRetrieval,
        "LLM" => Enums.TierLlmSemantic,
        "Fuzzy" => Enums.TierFuzzy,
        _ => p,
    };

    public async Task<ColumnMapping?> GetLatestMappingAsync(long sourceColumnId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT TOP (1) MappingId, SourceColumnId, TargetParameter, Confidence, Provenance, AutoApproved, ApprovedBy, CreatedAtUtc
              FROM ing.ColumnMapping WHERE SourceColumnId = @col AND IsCurrent = 1
             ORDER BY MappingVersion DESC, MappingId DESC
            """);
        cmd.Parameters.AddWithValue("@col", sourceColumnId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ColumnMapping
        {
            MappingId = L(r["MappingId"]),
            SourceColumnId = L(r["SourceColumnId"]),
            TargetParameter = S(r["TargetParameter"]),
            Confidence = DN(r["Confidence"]) ?? 0m,
            ProvenanceTier = MapTierBack(S(r["Provenance"])) ?? Enums.TierExactDictionary,
            AutoApproved = B(r["AutoApproved"]),
            ApprovedBy = SN(r["ApprovedBy"]),
            CreatedAtUtc = T(r["CreatedAtUtc"]),
        };
    }

    public async Task LearnSynonymAsync(ColumnSynonym syn)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            ;IF EXISTS (SELECT 1 FROM kb.MasterColumnSynonym WHERE NormalizedSynonym = @norm AND IsActive = 1)
                UPDATE kb.MasterColumnSynonym
                   SET UseCount = UseCount + 1, LastUsedAtUtc = SYSUTCDATETIME(),
                       LearnedFromApproval = CASE WHEN @learned = 1 THEN 1 ELSE LearnedFromApproval END
                 WHERE NormalizedSynonym = @norm AND IsActive = 1
            ELSE
                INSERT kb.MasterColumnSynonym (TargetParameter, SynonymText, NormalizedSynonym, Origin,
                                               LearnedFromApproval, LearnedFromCase, UseCount)
                SELECT @target, @text, @norm,
                       CASE WHEN @learned = 1 THEN N'LearnedFromApproval' ELSE N'Seed' END,
                       @learned, @case, 1
                WHERE NOT EXISTS (SELECT 1 FROM kb.MasterColumnSynonym WHERE NormalizedSynonym = @norm AND IsActive = 1);
            """);
        cmd.Parameters.AddWithValue("@norm", Normalize(syn.SynonymText));
        cmd.Parameters.AddWithValue("@target", syn.TargetParameter);
        cmd.Parameters.AddWithValue("@text", syn.SynonymText);
        cmd.Parameters.AddWithValue("@learned", syn.LearnedFromApproval);
        cmd.Parameters.AddWithValue("@case", (object?)syn.LearnedFromCase ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── clinical coding ───────────────────────────

    public async Task<List<ClaimCoding>> GetClaimCodingAsync(long caseId, string? codingStatus = null)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT ClaimCodingId, StdClaimId, CaseId, CodingVersion, IsCurrent,
                   Level1IcdCode, Level1DiseaseName, Level1Confidence, Level1Provenance,
                   Level2IcdCode, Level2DiseaseName, Level2Confidence, Level2Provenance,
                   DiseaseGroup, Department, IsChronic, BenefitGroup, MaternityDeliveryType,
                   ReasonText, NeedsHumanReview, CodingStatus, GoldenEntryId, CreatedAtUtc, UpdatedAtUtc
              FROM ing.ClaimCoding
             WHERE CaseId = @case AND IsCurrent = 1
               AND (@status IS NULL OR CodingStatus = @status)
             ORDER BY ClaimCodingId
            """);
        cmd.Parameters.AddWithValue("@case", caseId);
        cmd.Parameters.AddWithValue("@status", (object?)codingStatus ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ClaimCoding>();
        while (await r.ReadAsync())
            list.Add(new ClaimCoding
            {
                ClaimCodingId = L(r["ClaimCodingId"]),
                StdClaimId = L(r["StdClaimId"]),
                CaseId = L(r["CaseId"]),
                CodingVersion = I(r["CodingVersion"]),
                IsCurrent = B(r["IsCurrent"]),
                Level1IcdCode = SN(r["Level1IcdCode"]),
                Level1DiseaseName = SN(r["Level1DiseaseName"]),
                Level1Confidence = DN(r["Level1Confidence"]),
                Level1Provenance = SN(r["Level1Provenance"]),
                Level2IcdCode = SN(r["Level2IcdCode"]),
                Level2DiseaseName = SN(r["Level2DiseaseName"]),
                Level2Confidence = DN(r["Level2Confidence"]),
                Level2Provenance = SN(r["Level2Provenance"]),
                DiseaseGroup = SN(r["DiseaseGroup"]),
                Department = SN(r["Department"]),
                IsChronic = r["IsChronic"] is DBNull ? null : B(r["IsChronic"]),
                BenefitGroup = SN(r["BenefitGroup"]),
                MaternityDeliveryType = SN(r["MaternityDeliveryType"]),
                ReasonText = SN(r["ReasonText"]),
                NeedsHumanReview = B(r["NeedsHumanReview"]),
                CodingStatus = S(r["CodingStatus"]),
                GoldenEntryId = LN(r["GoldenEntryId"]),
                CreatedAtUtc = T(r["CreatedAtUtc"]),
                UpdatedAtUtc = T(r["UpdatedAtUtc"]),
            });
        return list;
    }

    public async Task<ClaimCoding> SaveClaimCodingAsync(ClaimCoding c)
    {
        await using var conn = await OpenAsync();
        if (c.ClaimCodingId == 0)
        {
            // New version: supersede the current row for this claim, then insert (one writer at a time is not
            // guaranteed here, so re-check IsCurrent in the UPDATE predicate).
            using var cmd = Cmd(conn, """
                ;UPDATE ing.ClaimCoding SET IsCurrent = 0, UpdatedAtUtc = SYSUTCDATETIME()
                  WHERE StdClaimId = @claim AND IsCurrent = 1;
                INSERT ing.ClaimCoding (StdClaimId, CaseId, CodingVersion, IsCurrent,
                    Level1IcdCode, Level1DiseaseName, Level1Confidence, Level1Provenance,
                    Level2IcdCode, Level2DiseaseName, Level2Confidence, Level2Provenance,
                    DiseaseGroup, Department, IsChronic, BenefitGroup, MaternityDeliveryType,
                    ReasonText, NeedsHumanReview, CodingStatus, GoldenEntryId)
                SELECT @claim, @case,
                       ISNULL((SELECT MAX(CodingVersion) + 1 FROM ing.ClaimCoding WHERE StdClaimId = @claim), 1),
                       1, @l1c, @l1n, @l1conf, @l1p, @l2c, @l2n, @l2conf, @l2p,
                       @grp, @dept, @chronic, @benefit, @mat, @reason, @review, @status, @golden;
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """);
            cmd.Parameters.AddWithValue("@claim", c.StdClaimId);
            cmd.Parameters.AddWithValue("@case", c.CaseId);
            cmd.Parameters.AddWithValue("@l1c", (object?)c.Level1IcdCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1n", (object?)c.Level1DiseaseName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1conf", (object?)c.Level1Confidence ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1p", (object?)c.Level1Provenance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2c", (object?)c.Level2IcdCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2n", (object?)c.Level2DiseaseName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2conf", (object?)c.Level2Confidence ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2p", (object?)c.Level2Provenance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@grp", (object?)c.DiseaseGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dept", (object?)c.Department ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@chronic", (object?)c.IsChronic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@benefit", (object?)c.BenefitGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mat", (object?)c.MaternityDeliveryType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@reason", (object?)c.ReasonText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@review", c.NeedsHumanReview);
            cmd.Parameters.AddWithValue("@status", c.CodingStatus);
            cmd.Parameters.AddWithValue("@golden", (object?)c.GoldenEntryId ?? DBNull.Value);
            c.ClaimCodingId = (long)(await cmd.ExecuteScalarAsync())!;
        }
        else
        {
            using var cmd = Cmd(conn, """
                UPDATE ing.ClaimCoding SET
                    Level1IcdCode = @l1c, Level1DiseaseName = @l1n, Level1Confidence = @l1conf, Level1Provenance = @l1p,
                    Level2IcdCode = @l2c, Level2DiseaseName = @l2n, Level2Confidence = @l2conf, Level2Provenance = @l2p,
                    DiseaseGroup = @grp, Department = @dept, IsChronic = @chronic, BenefitGroup = @benefit,
                    MaternityDeliveryType = @mat, ReasonText = @reason, NeedsHumanReview = @review,
                    CodingStatus = @status, GoldenEntryId = @golden, UpdatedAtUtc = SYSUTCDATETIME()
                 WHERE ClaimCodingId = @id
                """);
            cmd.Parameters.AddWithValue("@id", c.ClaimCodingId);
            cmd.Parameters.AddWithValue("@l1c", (object?)c.Level1IcdCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1n", (object?)c.Level1DiseaseName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1conf", (object?)c.Level1Confidence ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l1p", (object?)c.Level1Provenance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2c", (object?)c.Level2IcdCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2n", (object?)c.Level2DiseaseName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2conf", (object?)c.Level2Confidence ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l2p", (object?)c.Level2Provenance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@grp", (object?)c.DiseaseGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dept", (object?)c.Department ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@chronic", (object?)c.IsChronic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@benefit", (object?)c.BenefitGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mat", (object?)c.MaternityDeliveryType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@reason", (object?)c.ReasonText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@review", c.NeedsHumanReview);
            cmd.Parameters.AddWithValue("@status", c.CodingStatus);
            cmd.Parameters.AddWithValue("@golden", (object?)c.GoldenEntryId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        return c;
    }

    public async Task AddCodingReviewAsync(CodingReview review)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT ing.CodingReview (ClaimCodingId, Reviewer, Decision, SuggestedLevel1Json, SuggestedLevel2Json,
                                     FinalLevel1Json, FinalLevel2Json, Comments, PromotedToGolden)
            VALUES (@coding, @reviewer, @decision, @s1, @s2, @f1, @f2, @comments, @golden)
            """);
        cmd.Parameters.AddWithValue("@coding", review.ClaimCodingId);
        cmd.Parameters.AddWithValue("@reviewer", review.Reviewer);
        cmd.Parameters.AddWithValue("@decision", review.Decision);
        cmd.Parameters.AddWithValue("@s1", (object?)review.SuggestedLevel1Json ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s2", (object?)review.SuggestedLevel2Json ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@f1", (object?)review.FinalLevel1Json ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@f2", (object?)review.FinalLevel2Json ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@comments", (object?)review.Comments ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@golden", review.PromotedToGolden);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── knowledge ───────────────────────────

    public async Task<List<DiseaseEntry>> GetDiseasesAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT TaxonomyId, StandardDiseaseName, IcdCode, DiseaseGroup, IsChronic, MatchKeywordsJson FROM kb.DiseaseTaxonomy WHERE IsActive = 1");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<DiseaseEntry>();
        while (await r.ReadAsync())
            list.Add(new DiseaseEntry
            {
                DiseaseId = L(r["TaxonomyId"]),
                DiseaseName = S(r["StandardDiseaseName"]),
                DiseaseGroup = S(r["DiseaseGroup"]),
                IcdLevel1 = S(r["IcdCode"]),
                IsChronic = B(r["IsChronic"]),
                MatchKeywords = JArr(r["MatchKeywordsJson"]),
            });
        return list;
    }

    public async Task AddDiseaseAsync(DiseaseEntry d)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            IF NOT EXISTS (SELECT 1 FROM kb.DiseaseTaxonomy WHERE StandardDiseaseName = @name AND IcdCode = @icd AND IsActive = 1)
                INSERT kb.DiseaseTaxonomy (StandardDiseaseName, IcdCode, DiseaseGroup, IsChronic, MatchKeywordsJson)
                VALUES (@name, @icd, @group, @chronic, @kw)
            """);
        cmd.Parameters.AddWithValue("@name", d.DiseaseName);
        cmd.Parameters.AddWithValue("@icd", d.IcdLevel1 ?? "");
        cmd.Parameters.AddWithValue("@group", d.DiseaseGroup);
        cmd.Parameters.AddWithValue("@chronic", d.IsChronic);
        cmd.Parameters.AddWithValue("@kw", J(d.MatchKeywords));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<GoldenEntry>> GetGoldenEntriesAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT GoldenEntryId, GoldenVersion, NormalizedText, IcdLevel1, IcdLevel2, UseCount, WasCorrected, Source FROM kb.GoldenEntry WHERE IsActive = 1");
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<GoldenEntry>();
        while (await r.ReadAsync())
            list.Add(new GoldenEntry
            {
                GoldenEntryId = L(r["GoldenEntryId"]),
                GoldenVersion = S(r["GoldenVersion"]),
                RawTextNorm = S(r["NormalizedText"]),
                IcdLevel1 = S(r["IcdLevel1"]),
                IcdLevel2 = SN(r["IcdLevel2"]),
                UseCount = I(r["UseCount"]),
                WasCorrected = B(r["WasCorrected"]),
                Source = S(r["Source"]),
            });
        return list;
    }

    public async Task AddGoldenEntryAsync(GoldenEntry e)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT kb.GoldenEntry (GoldenVersion, SourceText, NormalizedText, IcdLevel1, IcdLevel2, WasCorrected, Source)
            SELECT @ver, @src, @norm, @l1, @l2, @corr, @source
            WHERE NOT EXISTS (SELECT 1 FROM kb.GoldenEntry WHERE NormalizedText = @norm AND IsActive = 1)
            """);
        cmd.Parameters.AddWithValue("@ver", e.GoldenVersion);
        cmd.Parameters.AddWithValue("@src", e.RawTextNorm);
        cmd.Parameters.AddWithValue("@norm", e.RawTextNorm);
        cmd.Parameters.AddWithValue("@l1", e.IcdLevel1);
        cmd.Parameters.AddWithValue("@l2", (object?)e.IcdLevel2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@corr", e.WasCorrected);
        cmd.Parameters.AddWithValue("@source", e.Source);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task IncrementGoldenUseAsync(long goldenEntryId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "UPDATE kb.GoldenEntry SET UseCount = UseCount + 1 WHERE GoldenEntryId = @id");
        cmd.Parameters.AddWithValue("@id", goldenEntryId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── pricing masters ───────────────────────────

    public async Task<List<PricingMasterVersion>> ListMasterVersionsAsync()
    {
        var list = new List<PricingMasterVersion>();
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT PricingMasterVersionId, VersionLabel, Status, EffectiveFrom, EffectiveTo FROM uw.PricingMasterVersion ORDER BY PricingMasterVersionId");
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PricingMasterVersion
            {
                MasterVersionId = I(r["PricingMasterVersionId"]),
                VersionRef = S(r["VersionLabel"]),
                Status = S(r["Status"]),
                EffectiveFromUtc = T(r["EffectiveFrom"]),
                EffectiveToUtc = TN(r["EffectiveTo"]),
            });
        }
        return list;
    }

    public async Task<PricingMasterVersion> GetActiveMasterVersionAsync()
    {
        await using var conn = await OpenAsync();
        int masterId;
        PricingMasterVersion v;
        using (var cmd = Cmd(conn, """
            SELECT TOP (1) PricingMasterVersionId, VersionLabel, Status, EffectiveFrom, EffectiveTo
              FROM uw.PricingMasterVersion WHERE Status = N'Active'
             ORDER BY EffectiveFrom DESC, PricingMasterVersionId DESC
            """))
        {
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                throw new InvalidOperationException("No active PricingMasterVersion — run SeedReferenceDataAsync or sql/DDL/09_seed.sql.");
            masterId = I(r["PricingMasterVersionId"]);
            v = new PricingMasterVersion
            {
                MasterVersionId = masterId,
                VersionRef = S(r["VersionLabel"]),
                Status = S(r["Status"]),
                EffectiveFromUtc = T(r["EffectiveFrom"]),
                EffectiveToUtc = TN(r["EffectiveTo"]),
            };
        }
        using (var cmd2 = Cmd(conn, "SELECT BandType, BandKey, BandValueJson FROM uw.PricingBand WHERE PricingMasterVersionId = @id ORDER BY BandType, DisplayOrder"))
        {
            cmd2.Parameters.AddWithValue("@id", masterId);
            using var r2 = await cmd2.ExecuteReaderAsync();
            while (await r2.ReadAsync())
            {
                var bandType = S(r2["BandType"]);
                var bandKey = S(r2["BandKey"]);
                var doc = Jd<Dictionary<string, JsonElement>>(S(r2["BandValueJson"]));
                var val = doc is not null && doc.TryGetValue("value", out var ve) ? ve.GetDecimal() : 0m;
                switch (bandType)
                {
                    case "MedicalTrend":
                        if (bandKey.Contains("46-60", StringComparison.OrdinalIgnoreCase)) v.MedicalTrendByAgeBand[1] = val;
                        else if (bandKey.Contains(">60", StringComparison.OrdinalIgnoreCase)) v.MedicalTrendByAgeBand[2] = val;
                        else v.MedicalTrendByAgeBand[0] = val;
                        break;
                    case "Ibnr":
                        if (bandKey.Equals("Default", StringComparison.OrdinalIgnoreCase)) v.IbnrPctByClaimBand = val;
                        break;
                    case "Credibility": v.CredibilityZ = val; break;
                    case "BenchmarkBurn": v.BenchmarkBurnPerLife = val; break;
                    case "RiskMargin": v.RiskMarginPct = val; break;
                    case "BaseExpense": v.BaseExpensePct = val; break;
                    case "LargeClaimNormalization": v.LargeClaimNormalizationPct = val; break;
                    case "StarRejection": v.StarRejectionPct = val; break;
                    case "Commission":
                        if (bandKey.Equals("Max", StringComparison.OrdinalIgnoreCase)) v.MaxCommissionPct = val;
                        else v.CommissionPct = val;
                        break;
                    case "Gst": v.GstPct = val; break;
                    case "MaxDiscount": v.MaxDiscountPct = val; break;
                    case "SicIncreaseCapture": v.SicIncreaseCapturePct = val; break;
                }
            }
        }
        return v;
    }

    public async Task UpdateMasterVersionAsync(PricingMasterVersion v)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            UPDATE uw.PricingMasterVersion
               SET Status = @status, EffectiveTo = @to
             WHERE PricingMasterVersionId = @id
            """);
        cmd.Parameters.AddWithValue("@status", v.Status);
        cmd.Parameters.AddWithValue("@to", (object?)v.EffectiveToUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", v.MasterVersionId);
        await cmd.ExecuteNonQueryAsync();
    }

public async Task AddMasterVersionAsync(PricingMasterVersion v)
    {
        await using var conn = await OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();

        using (var cmd = Cmd(conn, """
            INSERT uw.PricingMasterVersion (VersionLabel, Status, EffectiveFrom, Notes)
            OUTPUT INSERTED.PricingMasterVersionId
            VALUES (@label, @status, @from, @notes)
            """))
        {
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@label", v.VersionRef);
            cmd.Parameters.AddWithValue("@status", v.Status);
            cmd.Parameters.AddWithValue("@from", v.EffectiveFromUtc == default ? DateTime.UtcNow : v.EffectiveFromUtc);
            cmd.Parameters.AddWithValue("@notes", "Created via AddMasterVersionAsync (mirrors Core Pricing.cs defaults).");
            v.MasterVersionId = I(await cmd.ExecuteScalarAsync());
        }

        void AddBand(string type, string key, decimal value, string unit, int order)
        {
            using var b = Cmd(conn, """
                INSERT uw.PricingBand (PricingMasterVersionId, BandType, BandKey, BandValueJson, DisplayOrder)
                VALUES (@pmv, @type, @key, @json, @order)
                """);
            b.Transaction = tx;
            b.Parameters.AddWithValue("@pmv", v.MasterVersionId);
            b.Parameters.AddWithValue("@type", type);
            b.Parameters.AddWithValue("@key", key);
            b.Parameters.AddWithValue("@json", J(new { value, unit }));
            b.Parameters.AddWithValue("@order", order);
            b.ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }

        AddBand("MedicalTrend", "Age<=45", v.MedicalTrendByAgeBand[0], "Percent", 1);
        AddBand("MedicalTrend", "Age>60", v.MedicalTrendByAgeBand[2], "Percent", 2);
        AddBand("Ibnr", "Default", v.IbnrPctByClaimBand, "Percent", 1);
        AddBand("Credibility", "Default", v.CredibilityZ, "Factor", 1);
        AddBand("BenchmarkBurn", "Default", v.BenchmarkBurnPerLife, "AmountPerLife", 1);
        AddBand("RiskMargin", "Default", v.RiskMarginPct, "Percent", 1);
        AddBand("BaseExpense", "Default", v.BaseExpensePct, "Percent", 1);
        AddBand("LargeClaimNormalization", "Default", v.LargeClaimNormalizationPct, "Percent", 1);
        AddBand("StarRejection", "Default", v.StarRejectionPct, "Percent", 1);
        AddBand("Commission", "Default", v.CommissionPct, "Percent", 1);
        AddBand("Commission", "Max", v.MaxCommissionPct, "Percent", 2);
        AddBand("Gst", "Default", v.GstPct, "Percent", 1);
        AddBand("MaxDiscount", "Default", v.MaxDiscountPct, "Percent", 1);
            AddBand("SicIncreaseCapture", "Default", v.SicIncreaseCapturePct, "Percent", 1);

        await tx.CommitAsync();
    }

    // ─────────────────────────── guardrails ───────────────────────────

    public async Task<List<GuardrailRuleDef>> GetAllGuardrailRulesAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT RuleCode, RuleName, Category, MetricSelector, Operator, Threshold, Severity, MessageTemplate,
                   AppliesToExperienceBusiness, PctOfMetric, IsEnabled
              FROM uw.GuardrailRule ORDER BY GuardrailRuleId
            """);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<GuardrailRuleDef>();
        while (await r.ReadAsync())
            list.Add(new GuardrailRuleDef
            {
                RuleCode = S(r["RuleCode"]),
                RuleName = S(r["RuleName"]) is { Length: > 0 } rn ? rn : null,
                Category = S(r["Category"]),
                MetricSelector = S(r["MetricSelector"]),
                Operator = S(r["Operator"]),
                Threshold = D(r["Threshold"]),
                Severity = S(r["Severity"]),
                MessageTemplate = S(r["MessageTemplate"]) ?? "",
                AppliesToExperienceBusiness = B(r["AppliesToExperienceBusiness"]),
                PctOfMetric = S(r["PctOfMetric"]) is { Length: > 0 } pm ? pm : null,
                IsEnabled = B(r["IsEnabled"]),
            });
        return list;
    }

    public async Task<List<GuardrailRuleDef>> GetGuardrailRulesAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT RuleCode, Category, MetricSelector, Operator, Threshold, Severity, MessageTemplate,
                   AppliesToExperienceBusiness, PctOfMetric
              FROM uw.GuardrailRule WHERE IsEnabled = 1 ORDER BY GuardrailRuleId
            """);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<GuardrailRuleDef>();
        while (await r.ReadAsync())
            list.Add(new GuardrailRuleDef
            {
                RuleCode = S(r["RuleCode"]),
                Category = S(r["Category"]),
                MetricSelector = S(r["MetricSelector"]),
                Operator = S(r["Operator"]),
                Threshold = D(r["Threshold"]),
                Severity = S(r["Severity"]),
                MessageTemplate = S(r["MessageTemplate"]),
                AppliesToExperienceBusiness = B(r["AppliesToExperienceBusiness"]),
                PctOfMetric = SN(r["PctOfMetric"]),
            });
        return list;
    }

    public async Task UpsertGuardrailRuleAsync(GuardrailRuleDef rule)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            ;IF EXISTS (SELECT 1 FROM uw.GuardrailRule WHERE RuleCode = @code)
                UPDATE uw.GuardrailRule
                   SET Category = @cat, MetricSelector = @sel, Operator = @op, Threshold = @thr,
                       Severity = @sev, MessageTemplate = @msg, AppliesToExperienceBusiness = @exp,
                       PctOfMetric = @pct, IsEnabled = @en,
                       RuleName = COALESCE(@name, RuleName), UpdatedAtUtc = SYSUTCDATETIME()
                 WHERE RuleCode = @code
            ELSE
                INSERT uw.GuardrailRule (RuleCode, RuleName, Category, MetricSelector, Operator, Threshold,
                                         Severity, MessageTemplate, AppliesToExperienceBusiness, PctOfMetric, IsEnabled)
                VALUES (@code, COALESCE(@name, @code), @cat, @sel, @op, @thr, @sev, @msg, @exp, @pct, @en);
            """);
        cmd.Parameters.AddWithValue("@code", rule.RuleCode);
        cmd.Parameters.AddWithValue("@name", (object?)rule.RuleName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@en", rule.IsEnabled);
        cmd.Parameters.AddWithValue("@cat", rule.Category);
        cmd.Parameters.AddWithValue("@sel", rule.MetricSelector);
        cmd.Parameters.AddWithValue("@op", rule.Operator);
        cmd.Parameters.AddWithValue("@thr", rule.Threshold);
        cmd.Parameters.AddWithValue("@sev", rule.Severity);
        cmd.Parameters.AddWithValue("@msg", rule.MessageTemplate);
        cmd.Parameters.AddWithValue("@exp", rule.AppliesToExperienceBusiness);
        cmd.Parameters.AddWithValue("@pct", (object?)rule.PctOfMetric ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────── reference ───────────────────────────

    public async Task<Dictionary<string, string>> GetPaidStatusMapAsync()
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "SELECT RawStatusText, NormalizedStatus FROM ref.PaidStatusDefinition WHERE IsActive = 1 AND InsurerId IS NULL AND TpaId IS NULL");
        using var r = await cmd.ExecuteReaderAsync();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await r.ReadAsync())
        {
            var raw = S(r["RawStatusText"]);
            if (!map.ContainsKey(raw)) map[raw] = S(r["NormalizedStatus"]);
        }
        return map;
    }
}
