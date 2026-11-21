using System.Net;
using System.Net.Mail;
using BijliGuard.Application.Abstractions;
using BijliGuard.Domain.Auth;
using Microsoft.Extensions.Options;

namespace BijliGuard.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" for real delivery, "DevelopmentOutbox" to write messages to a local file (Development only).</summary>
    public string Provider { get; set; } = "DevelopmentOutbox";
    public string FromAddress { get; set; } = "no-reply@bijliguard.local";
    public string FromName { get; set; } = "BijliGuard";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OutboxPath { get; set; } = "logs/dev-outbox.txt";
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendOtpAsync(string toEmail, string code, TimeSpan validFor, OtpPurpose purpose, CancellationToken ct)
    {
        using var message = OtpEmail.Build(_options, toEmail, code, validFor, purpose);
        var mime = MimeKit.MimeMessage.CreateFromMailMessage(message);

        // Port 465 uses TLS from the first byte (implicit TLS). Other ports upgrade with STARTTLS.
        // MailKit does both; the built-in SmtpClient cannot do implicit TLS.
        var security = !_options.UseSsl
            ? MailKit.Security.SecureSocketOptions.None
            : _options.Port == 465
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls;

        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, security, ct);
        await client.AuthenticateAsync(_options.Username, _options.Password, ct);
        await MailKit.IMailTransportExtensions.SendAsync(client, mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Development-only sender. Writes the code to a local file so the flow can be tested without SMTP.</summary>
public sealed class DevelopmentOutboxEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public DevelopmentOutboxEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendOtpAsync(string toEmail, string code, TimeSpan validFor, OtpPurpose purpose, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.OutboxPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var line = $"{DateTimeOffset.UtcNow:O} | purpose={purpose} | to={toEmail} | code={code} | valid={validFor.TotalMinutes} min{Environment.NewLine}";
        await File.AppendAllTextAsync(_options.OutboxPath, line, ct);
    }
}

internal static class OtpEmail
{
    public static MailMessage Build(EmailOptions options, string toEmail, string code, TimeSpan validFor, OtpPurpose purpose)
    {
        var (subject, action) = purpose switch
        {
            OtpPurpose.PasswordReset => ("Reset your BijliGuard password", "reset your password"),
            _ => ("Your BijliGuard verification code", "verify your email"),
        };

        var message = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = subject,
            Body = $"Use this code to {action}: {code}\n\n" +
                   $"It expires in {(int)validFor.TotalMinutes} minutes and can be used once.\n" +
                   "If you did not ask for this, you can ignore this email. Your password stays the same.",
            IsBodyHtml = false
        };
        message.To.Add(toEmail);
        return message;
    }
}
