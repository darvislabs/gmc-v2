using Microsoft.Data.SqlClient;
using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Baseline SQL persistence for sys.AppUser + com.Notification (workflow §3, §12).
/// Creates its tables idempotently on first use; the DDL agent owns the canonical sql/DDL files.
/// Delegates all non-user methods to the wrapped SqlRepository.
/// </summary>
public partial class SqlRepository
{
    private bool _userTablesChecked;

    private async Task EnsureUserTablesAsync(SqlConnection conn)
    {
        if (_userTablesChecked) return;
        using var cmd = Cmd(conn, """
            IF OBJECT_ID(N'sys.AppUser', N'U') IS NULL
            CREATE TABLE sys.AppUser(
                AppUserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_AppUser PRIMARY KEY,
                Email NVARCHAR(200) NOT NULL CONSTRAINT UQ_AppUser_Email UNIQUE,
                DisplayName NVARCHAR(120) NOT NULL,
                Role NVARCHAR(30) NOT NULL,
                PasswordHash NVARCHAR(300) NOT NULL,
                IsActive BIT NOT NULL DEFAULT(1),
                CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                LastLoginUtc DATETIME2 NULL);
            IF OBJECT_ID(N'com.Notification', N'U') IS NULL
            CREATE TABLE com.Notification(
                NotificationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_com_Notification PRIMARY KEY,
                TargetRole NVARCHAR(30) NULL,
                TargetUserId INT NULL,
                CaseId INT NULL,
                CaseNumber NVARCHAR(30) NULL,
                Kind NVARCHAR(40) NOT NULL,
                Title NVARCHAR(200) NOT NULL,
                Body NVARCHAR(MAX) NULL,
                LinkPath NVARCHAR(200) NULL,
                IsRead BIT NOT NULL DEFAULT(0),
                CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
            """);
        await cmd.ExecuteNonQueryAsync();
        _userTablesChecked = true;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "SELECT AppUserId, Email, DisplayName, Role, PasswordHash, IsActive, CreatedAtUtc, LastLoginUtc FROM sys.AppUser WHERE Email = @e");
        cmd.Parameters.AddWithValue("@e", email);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapUser(r) : null;
    }

    public async Task<User?> GetUserAsync(long userId)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "SELECT AppUserId, Email, DisplayName, Role, PasswordHash, IsActive, CreatedAtUtc, LastLoginUtc FROM sys.AppUser WHERE AppUserId = @id");
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapUser(r) : null;
    }

    public async Task<User> AddUserAsync(User u)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, """
            INSERT sys.AppUser (Email, DisplayName, Role, PasswordHash, IsActive)
            OUTPUT INSERTED.AppUserId
            VALUES (@e, @dn, @role, @ph, @active)
            """);
        cmd.Parameters.AddWithValue("@e", u.Email);
        cmd.Parameters.AddWithValue("@dn", u.DisplayName);
        cmd.Parameters.AddWithValue("@role", u.Role);
        cmd.Parameters.AddWithValue("@ph", u.PasswordHash);
        cmd.Parameters.AddWithValue("@active", u.IsActive);
        u.UserId = I(await cmd.ExecuteScalarAsync());
        return u;
    }

    public async Task UpdateUserAsync(User u)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "UPDATE sys.AppUser SET DisplayName = @dn, Role = @role, IsActive = @active, PasswordHash = @ph WHERE AppUserId = @id");
        cmd.Parameters.AddWithValue("@dn", u.DisplayName);
        cmd.Parameters.AddWithValue("@role", u.Role);
        cmd.Parameters.AddWithValue("@active", u.IsActive);
        cmd.Parameters.AddWithValue("@ph", u.PasswordHash);
        cmd.Parameters.AddWithValue("@id", u.UserId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<User>> ListUsersAsync()
    {
        var list = new List<User>();
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "SELECT AppUserId, Email, DisplayName, Role, PasswordHash, IsActive, CreatedAtUtc, LastLoginUtc FROM sys.AppUser ORDER BY AppUserId");
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapUser(r));
        return list;
    }

    public async Task UpdateLastLoginAsync(long userId)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "UPDATE sys.AppUser SET LastLoginUtc = SYSUTCDATETIME() WHERE AppUserId = @id");
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddNotificationAsync(Notification n)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, """
            INSERT com.Notification (TargetRole, TargetUserId, CaseId, CaseNumber, Kind, Title, Body, LinkPath)
            OUTPUT INSERTED.NotificationId
            VALUES (@role, @uid, @cid, @cno, @kind, @title, @body, @link)
            """);
        cmd.Parameters.AddWithValue("@role", (object?)n.TargetRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", (object?)n.TargetUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", (object?)n.CaseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cno", (object?)n.CaseNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kind", n.Kind);
        cmd.Parameters.AddWithValue("@title", n.Title);
        cmd.Parameters.AddWithValue("@body", (object?)n.Body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@link", (object?)n.LinkPath ?? DBNull.Value);
        n.NotificationId = I(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Notification>> GetNotificationsAsync(string? role, long? userId, bool unreadOnly = false)
    {
        var list = new List<Notification>();
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, """
            SELECT TOP (200) NotificationId, TargetRole, TargetUserId, CaseId, CaseNumber, Kind, Title, Body, LinkPath, IsRead, CreatedAtUtc
              FROM com.Notification
             WHERE (TargetUserId IS NOT NULL AND TargetUserId = @uid)
                OR (TargetUserId IS NULL AND TargetRole IS NOT NULL AND TargetRole = @role)
                OR (TargetUserId IS NULL AND TargetRole IS NULL)
             ORDER BY NotificationId DESC
            """);
        cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var n = new Notification
            {
                NotificationId = I(r["NotificationId"]),
                TargetRole = SN(r["TargetRole"]) ?? "",
                TargetUserId = r["TargetUserId"] is DBNull ? null : (long)I(r["TargetUserId"]),
                CaseId = r["CaseId"] is DBNull ? null : (long)I(r["CaseId"]),
                CaseNumber = SN(r["CaseNumber"]) ?? "",
                Kind = S(r["Kind"]),
                Title = S(r["Title"]),
                Body = SN(r["Body"]) ?? "",
                LinkPath = SN(r["LinkPath"]),
                IsRead = (bool)r["IsRead"],
                CreatedAtUtc = T(r["CreatedAtUtc"]),
            };
            if (!unreadOnly || !n.IsRead) list.Add(n);
        }
        return list;
    }

    public async Task MarkNotificationReadAsync(long notificationId)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, "UPDATE com.Notification SET IsRead = 1 WHERE NotificationId = @id");
        cmd.Parameters.AddWithValue("@id", notificationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkAllNotificationsReadAsync(string? role, long? userId)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, """
            UPDATE com.Notification SET IsRead = 1
             WHERE IsRead = 0
               AND ((TargetUserId IS NOT NULL AND TargetUserId = @uid)
                 OR (TargetUserId IS NULL AND TargetRole IS NOT NULL AND TargetRole = @role)
                 OR (TargetUserId IS NULL AND TargetRole IS NULL))
            """);
        cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static User MapUser(SqlDataReader r) => new()
    {
        UserId = I(r["AppUserId"]),
        Email = S(r["Email"]),
        DisplayName = S(r["DisplayName"]),
        Role = S(r["Role"]),
        PasswordHash = S(r["PasswordHash"]),
        IsActive = (bool)r["IsActive"],
        CreatedAtUtc = T(r["CreatedAtUtc"]),
        LastLoginUtc = TN(r["LastLoginUtc"]),
    };
}
