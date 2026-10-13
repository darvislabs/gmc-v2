using Microsoft.Data.SqlClient;
using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Canonical SQL persistence for users + notifications, using the DDL's real tables:
///   ref.[User] / ref.[UserRole] / ref.[Role]  (users + role assignment)
///   ops.Notification                           (in-app notification center)
/// Password hashes are PBKDF2 strings stored in PasswordHash (salt embedded in the hash string).
/// Seeds the five role users idempotently on first use (workflow §3).
/// </summary>
public partial class SqlRepository
{
    private bool _userTablesChecked;

    private async Task EnsureUserTablesAsync(SqlConnection conn)
    {
        if (_userTablesChecked) return;
        using var cmd = Cmd(conn, "");
        foreach (var seed in UserSeed.SeedUsers())
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT COUNT(*) FROM ref.[User] WHERE Email = @e";
            cmd.Parameters.AddWithValue("@e", seed.Email);
            if ((int)(await cmd.ExecuteScalarAsync())! > 0) continue;

            cmd.CommandText = "INSERT ref.[User] (UserName, DisplayName, Email, PasswordHash, IsActive, CreatedBy) VALUES (@un, @dn, @e, @ph, 1, N'system-seed'); SELECT CAST(SCOPE_IDENTITY() AS INT);";
            cmd.Parameters.AddWithValue("@un", seed.Email);
            cmd.Parameters.AddWithValue("@dn", seed.DisplayName);
            cmd.Parameters.AddWithValue("@ph", seed.PasswordHash);
            var uid = (int)(await cmd.ExecuteScalarAsync())!;

            cmd.CommandText = "INSERT ref.[UserRole] (UserId, RoleId) SELECT @uid, RoleId FROM ref.[Role] WHERE RoleName = @role";
            cmd.Parameters.AddWithValue("@uid", uid);
            cmd.Parameters.AddWithValue("@role", MapRole(seed.Role));
            await cmd.ExecuteNonQueryAsync();
        }
        _userTablesChecked = true;
    }

    /// <summary>App role names → canonical ref.Role names (SalesAgent→SalesPerson, VP→ChiefUnderwriter).</summary>
    private static string MapRole(string appRole) => appRole switch
    {
        Enums.RoleSalesAgent => "SalesPerson",
        Enums.RoleUnderwriter => "Underwriter",
        Enums.RoleSeniorUW => "SeniorUnderwriter",
        Enums.RoleVP => "ChiefUnderwriter",
        _ => "Admin",
    };

    private const string UserSelect = """
        SELECT u.UserId, u.Email, u.DisplayName, r.RoleName AS Role, u.PasswordHash, u.IsActive, u.CreatedAtUtc, u.LastLoginAtUtc
          FROM ref.[User] u
          LEFT JOIN ref.[UserRole] ur ON ur.UserId = u.UserId
          LEFT JOIN ref.[Role] r ON r.RoleId = ur.RoleId
        """;

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, UserSelect + " WHERE u.Email = @e");
        cmd.Parameters.AddWithValue("@e", email);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapUser(r) : null;
    }

    public async Task<User?> GetUserAsync(long userId)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, UserSelect + " WHERE u.UserId = @id");
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapUser(r) : null;
    }

    public async Task<User> AddUserAsync(User u)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        int uid;
        using (var cmd = Cmd(conn, "INSERT ref.[User] (UserName, DisplayName, Email, PasswordHash, IsActive, CreatedBy) VALUES (@un, @dn, @e, @ph, @active, N'console'); SELECT CAST(SCOPE_IDENTITY() AS INT);"))
        {
            cmd.Parameters.AddWithValue("@un", u.Email);
            cmd.Parameters.AddWithValue("@dn", u.DisplayName);
            cmd.Parameters.AddWithValue("@e", u.Email);
            cmd.Parameters.AddWithValue("@ph", u.PasswordHash);
            cmd.Parameters.AddWithValue("@active", u.IsActive);
            uid = (int)(await cmd.ExecuteScalarAsync())!;
        }
        using (var cmd = Cmd(conn, "INSERT ref.[UserRole] (UserId, RoleId) SELECT @uid, RoleId FROM ref.[Role] WHERE RoleName = @role"))
        {
            cmd.Parameters.AddWithValue("@uid", uid);
            cmd.Parameters.AddWithValue("@role", MapRole(u.Role));
            await cmd.ExecuteNonQueryAsync();
        }
        u.UserId = uid;
        return u;
    }

    public async Task UpdateUserAsync(User u)
    {
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using (var cmd = Cmd(conn, "UPDATE ref.[User] SET DisplayName = @dn, IsActive = @active, PasswordHash = @ph, UpdatedAtUtc = SYSUTCDATETIME() WHERE UserId = @id"))
        {
            cmd.Parameters.AddWithValue("@dn", u.DisplayName);
            cmd.Parameters.AddWithValue("@active", u.IsActive);
            cmd.Parameters.AddWithValue("@ph", u.PasswordHash);
            cmd.Parameters.AddWithValue("@id", u.UserId);
            await cmd.ExecuteNonQueryAsync();
        }
        if (u.Role is { Length: > 0 })
        {
            using var cmd = Cmd(conn, "UPDATE ur SET ur.RoleId = r.RoleId FROM ref.[UserRole] ur JOIN ref.[Role] r ON r.RoleName = @role WHERE ur.UserId = @uid; IF @@ROWCOUNT = 0 INSERT ref.[UserRole] (UserId, RoleId) SELECT @uid, r.RoleId FROM ref.[Role] r WHERE r.RoleName = @role;");
            cmd.Parameters.AddWithValue("@uid", u.UserId);
            cmd.Parameters.AddWithValue("@role", MapRole(u.Role));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<User>> ListUsersAsync()
    {
        var list = new List<User>();
        await using var conn = await OpenAsync();
        await EnsureUserTablesAsync(conn);
        using var cmd = Cmd(conn, UserSelect + " ORDER BY u.UserId");
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapUser(r));
        return list;
    }

    public async Task UpdateLastLoginAsync(long userId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "UPDATE ref.[User] SET LastLoginAtUtc = SYSUTCDATETIME() WHERE UserId = @id");
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddNotificationAsync(Notification n)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            INSERT ops.Notification (RecipientUserId, Recipient, CaseId, NotificationType, Channel, Subject, Body, Status, SentAtUtc)
            OUTPUT INSERTED.NotificationId
            VALUES (@uid, @recipient, @cid, @kind, N'InApp', @title, @body, N'Sent', SYSUTCDATETIME())
            """);
        cmd.Parameters.AddWithValue("@uid", n.TargetUserId is { } tu ? (object)(int)tu : DBNull.Value);
        cmd.Parameters.AddWithValue("@recipient", n.TargetRole is { Length: > 0 } ? (object)n.TargetRole : DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", n.CaseId is { } cid ? (object)(int)cid : DBNull.Value);
        cmd.Parameters.AddWithValue("@kind", n.Kind);
        cmd.Parameters.AddWithValue("@title", n.Title);
        cmd.Parameters.AddWithValue("@body", (object?)n.Body ?? DBNull.Value);
        n.NotificationId = I(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Notification>> GetNotificationsAsync(string? role, long? userId, bool unreadOnly = false)
    {
        var list = new List<Notification>();
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            SELECT TOP (200) NotificationId, RecipientUserId, Recipient, CaseId, NotificationType, Subject, Body, ReadAtUtc, CreatedAtUtc
              FROM ops.Notification
             WHERE (RecipientUserId IS NOT NULL AND RecipientUserId = @uid)
                OR (RecipientUserId IS NULL AND Recipient IS NOT NULL AND Recipient = @role)
                OR (RecipientUserId IS NULL AND (Recipient IS NULL OR Recipient = N''))
             ORDER BY NotificationId DESC
            """);
        cmd.Parameters.AddWithValue("@uid", userId is { } tu ? (object)(int)tu : DBNull.Value);
        cmd.Parameters.AddWithValue("@role", role is { Length: > 0 } ? (object)role : DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var n = new Notification
            {
                NotificationId = L(r["NotificationId"]),
                TargetUserId = r["RecipientUserId"] is DBNull ? null : L(r["RecipientUserId"]),
                TargetRole = SN(r["Recipient"]) ?? "",
                CaseId = r["CaseId"] is DBNull ? null : L(r["CaseId"]),
                Kind = S(r["NotificationType"]),
                Title = SN(r["Subject"]) ?? "",
                Body = SN(r["Body"]) ?? "",
                IsRead = r["ReadAtUtc"] is not DBNull,
                CreatedAtUtc = T(r["CreatedAtUtc"]),
            };
            if (!unreadOnly || !n.IsRead) list.Add(n);
        }
        return list;
    }

    public async Task MarkNotificationReadAsync(long notificationId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, "UPDATE ops.Notification SET ReadAtUtc = SYSUTCDATETIME(), Status = N'Read' WHERE NotificationId = @id AND ReadAtUtc IS NULL");
        cmd.Parameters.AddWithValue("@id", notificationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkAllNotificationsReadAsync(string? role, long? userId)
    {
        await using var conn = await OpenAsync();
        using var cmd = Cmd(conn, """
            UPDATE ops.Notification SET ReadAtUtc = SYSUTCDATETIME(), Status = N'Read'
             WHERE ReadAtUtc IS NULL
               AND ((RecipientUserId IS NOT NULL AND RecipientUserId = @uid)
                 OR (RecipientUserId IS NULL AND Recipient IS NOT NULL AND Recipient = @role)
                 OR (RecipientUserId IS NULL AND (Recipient IS NULL OR Recipient = N'')))
            """);
        cmd.Parameters.AddWithValue("@uid", userId is { } tu ? (object)(int)tu : DBNull.Value);
        cmd.Parameters.AddWithValue("@role", role is { Length: > 0 } ? (object)role : DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static User MapUser(SqlDataReader r) => new()
    {
        UserId = I(r["UserId"]),
        Email = S(r["Email"]),
        DisplayName = SN(r["DisplayName"]) ?? "",
        Role = SN(r["Role"]) ?? "SalesPerson",
        PasswordHash = SN(r["PasswordHash"]) ?? "",
        IsActive = (bool)r["IsActive"],
        CreatedAtUtc = T(r["CreatedAtUtc"]),
        LastLoginUtc = TN(r["LastLoginAtUtc"]),
    };
}
