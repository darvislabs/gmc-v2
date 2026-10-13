using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>In-memory user + notification store (baseline; SQL versions land with the persistence agent).</summary>
public static class UserSeed
{
    /// <summary>Seeds the five role users (workflow §3). Password = Role@12345 (e.g. Sales@12345).</summary>
    public static List<User> SeedUsers() => new()
    {
        new() { Email = "sales@opexminds.com",   DisplayName = "Sam · Sales",       Role = Enums.RoleSalesAgent, PasswordHash = PasswordHasher.Hash("Sales@12345") },
        new() { Email = "uw@opexminds.com",      DisplayName = "Urvashi · UW",      Role = Enums.RoleUnderwriter, PasswordHash = PasswordHasher.Hash("Uw@12345") },
        new() { Email = "senior@opexminds.com",  DisplayName = "Sanjay · Senior UW", Role = Enums.RoleSeniorUW, PasswordHash = PasswordHasher.Hash("Senior@12345") },
        new() { Email = "vp@opexminds.com",      DisplayName = "Priya · VP",        Role = Enums.RoleVP, PasswordHash = PasswordHasher.Hash("Vp@12345") },
        new() { Email = "admin@opexminds.com",   DisplayName = "Admin",             Role = Enums.RoleAdmin, PasswordHash = PasswordHasher.Hash("Admin@12345") },
    };
}

public sealed class InMemoryUserStore
{
    private readonly object _gate = new();
    private readonly List<User> _users = new();
    private readonly List<Notification> _notifications = new();
    private long _userId = 0, _notifId = 0;

    public InMemoryUserStore()
    {
        foreach (var u in UserSeed.SeedUsers())
        {
            u.UserId = ++_userId;
            _users.Add(u);
        }
    }

    public Task<User?> GetUserByEmailAsync(string email)
    {
        lock (_gate) { return Task.FromResult(_users.FirstOrDefault(u => u.Email == email)); }
    }

    public Task<User?> GetUserAsync(long userId)
    {
        lock (_gate) { return Task.FromResult(_users.FirstOrDefault(u => u.UserId == userId)); }
    }

    public Task<User> AddUserAsync(User u)
    {
        lock (_gate)
        {
            u.UserId = ++_userId;
            _users.Add(u);
            return Task.FromResult(u);
        }
    }

    public Task UpdateUserAsync(User u)
    {
        lock (_gate)
        {
            var i = _users.FindIndex(x => x.UserId == u.UserId);
            if (i >= 0) _users[i] = u;
            return Task.CompletedTask;
        }
    }

    public Task<List<User>> ListUsersAsync()
    {
        lock (_gate) { return Task.FromResult(_users.Select(u => u).ToList()); }
    }

    public Task UpdateLastLoginAsync(long userId)
    {
        lock (_gate)
        {
            var u = _users.FirstOrDefault(x => x.UserId == userId);
            if (u is not null) u.LastLoginUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }

    public Task AddNotificationAsync(Notification n)
    {
        lock (_gate)
        {
            n.NotificationId = ++_notifId;
            _notifications.Add(n);
            return Task.CompletedTask;
        }
    }

    public Task<List<Notification>> GetNotificationsAsync(string? role, long? userId, bool unreadOnly = false)
    {
        lock (_gate)
        {
            var q = _notifications.Where(n =>
                (n.TargetUserId is { } tu && tu == userId) ||
                (n.TargetUserId is null && !string.IsNullOrEmpty(n.TargetRole) && n.TargetRole == role) ||
                (n.TargetUserId is null && string.IsNullOrEmpty(n.TargetRole)));
            if (unreadOnly) q = q.Where(n => !n.IsRead);
            return Task.FromResult(q.OrderByDescending(n => n.CreatedAtUtc).Take(200).ToList());
        }
    }

    public Task MarkNotificationReadAsync(long notificationId)
    {
        lock (_gate)
        {
            var n = _notifications.FirstOrDefault(x => x.NotificationId == notificationId);
            if (n is not null) n.IsRead = true;
            return Task.CompletedTask;
        }
    }

    public Task<int> MarkAllNotificationsReadAsync(string? role, long? userId)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (var n in _notifications.Where(n =>
                (n.TargetUserId is { } tu && tu == userId) ||
                (n.TargetUserId is null && !string.IsNullOrEmpty(n.TargetRole) && n.TargetRole == role) ||
                (n.TargetUserId is null && string.IsNullOrEmpty(n.TargetRole)) && !n.IsRead))
            {
                n.IsRead = true;
                count++;
            }
            return Task.FromResult(count);
        }
    }
}

/// <summary>Mixes user/notification storage into InMemoryRepository (partial class in Infrastructure).</summary>
public static class InMemoryUserStoreExtensions
{
    private static readonly Dictionary<InMemoryRepository, InMemoryUserStore> Stores = new();
    private static readonly object Gate = new();

    public static InMemoryUserStore UserStore(this InMemoryRepository repo)
    {
        lock (Gate)
        {
            if (!Stores.TryGetValue(repo, out var s)) { s = new InMemoryUserStore(); Stores[repo] = s; }
            return s;
        }
    }
}
