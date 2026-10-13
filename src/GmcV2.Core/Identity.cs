namespace GmcV2.Core;

/// <summary>Console identity + RBAC (workflow §3 actors, §9 authority). Backed by sys.AppUser.</summary>
public sealed class User
{
    public long UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = Enums.RoleSalesAgent;
    public string PasswordHash { get; set; } = "";   // PBKDF2 iterations.salt.hash (base64 parts)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
}

/// <summary>In-app notification (workflow §12 matrix). Backed by com.Notification.</summary>
public sealed class Notification
{
    public long NotificationId { get; set; }
    /// <summary>Target role for broadcast, or empty when addressed to a single user.</summary>
    public string TargetRole { get; set; } = "";
    public long? TargetUserId { get; set; }
    public long? CaseId { get; set; }
    public string CaseNumber { get; set; } = "";
    public string Kind { get; set; } = "";       // CaseAutoQuoted | CaseReferred | ExceptionRaised | ExceptionResolved | QuoteSuperseded | DiscountRequested | AiDegraded
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? LinkPath { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Parsed file content — structure only, never interpreted (architecture: extraction is mechanical).</summary>
public sealed class ParsedSheet
{
    public string SheetName { get; set; } = "";
    public List<string> Headers { get; set; } = new();
    /// <summary>All data rows as string cells (empty string for blanks), aligned to Headers.</summary>
    public List<string[]> Rows { get; set; } = new();
}

public sealed class ParsedDocument
{
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "";     // xlsx | csv | pdf | unknown
    public List<ParsedSheet> Sheets { get; set; } = new();
    /// <summary>Free-text lines extracted from PDF (schedule extraction works over these).</summary>
    public List<string> TextLines { get; set; } = new();
    public string? Error { get; set; }
}
