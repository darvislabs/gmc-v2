using GmcV2.Core;

namespace GmcV2.Infrastructure;

/// <summary>
/// Email fan-out (workflow §12): quote published, referral raised, exception assigned,
/// revision requested, decision recorded. Transport is env-driven:
///   GMCV2_SMTP_HOST + GMCV2_SMTP_PORT + GMCV2_SMTP_USER + GMCV2_SMTP_PASS + GMCV2_SMTP_FROM  → real SMTP
///   anything else → log transport (writes to console + ops.AuditLog via caller); never throws.
/// recipients come from Notification rows; this service SENDS, the notifier decides WHO.
/// </summary>
public interface IEmailTransport
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string body);
}

public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _pass;
    private readonly string _from;

    public SmtpEmailTransport(string host, int port, string? user, string? pass, string from)
    { _host = host; _port = port; _user = user; _pass = pass; _from = from; }

    public bool IsConfigured => true;

    public async Task SendAsync(string to, string subject, string body)
    {
        // System.Net.Mail.SmtpClient is obsolete-but-functional; MailKit would add a dep —
        // keep zero-dep for now, swap when a provider account exists.
        using var client = new System.Net.Mail.SmtpClient(_host, _port)
        {
            EnableSsl = true,
            Credentials = _user is null ? null : new System.Net.NetworkCredential(_user, _pass ?? ""),
        };
        using var msg = new System.Net.Mail.MailMessage(_from, to, subject, body);
        await client.SendMailAsync(msg);
    }
}

public sealed class LogEmailTransport : IEmailTransport
{
    public bool IsConfigured => false;
    public Task SendAsync(string to, string subject, string body)
    {
        Console.Error.WriteLine($"[EmailTransport:log] to={to} subject={subject}");
        return Task.CompletedTask;
    }
}

public static class EmailTransportFactory
{
    /// <summary>Env-driven: real SMTP when GMCV2_SMTP_HOST is set, log transport otherwise. Never throws.</summary>
    public static IEmailTransport FromEnv()
    {
        var host = Environment.GetEnvironmentVariable("GMCV2_SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host)) return new LogEmailTransport();
        var port = int.TryParse(Environment.GetEnvironmentVariable("GMCV2_SMTP_PORT"), out var p) ? p : 587;
        return new SmtpEmailTransport(
            host, port,
            Environment.GetEnvironmentVariable("GMCV2_SMTP_USER"),
            Environment.GetEnvironmentVariable("GMCV2_SMTP_PASS"),
            Environment.GetEnvironmentVariable("GMCV2_SMTP_FROM") ?? "gmc@opexminds.com");
    }
}
