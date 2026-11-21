namespace BijliGuard.Domain.Auth;

/// <summary>Business rules for one-time passwords. Pure: no I/O, no clock access.</summary>
public static class OtpPolicy
{
    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public static bool CanResend(DateTimeOffset? lastIssuedAt, DateTimeOffset now) =>
        lastIssuedAt is null || now - lastIssuedAt.Value >= ResendCooldown;
}
