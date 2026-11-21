namespace BijliGuard.Domain.Auth;

public sealed class EmailOtp
{
    private EmailOtp()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public OtpPurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static EmailOtp Issue(Guid userId, OtpPurpose purpose, string codeHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            CodeHash = codeHash,
            IssuedAt = now,
            ExpiresAt = now + OtpPolicy.Lifetime,
            FailedAttempts = 0
        };

    public OtpCheckResult Check(DateTimeOffset now)
    {
        if (ConsumedAt is not null) return OtpCheckResult.NotFound;
        if (now >= ExpiresAt) return OtpCheckResult.Expired;
        if (FailedAttempts >= OtpPolicy.MaxFailedAttempts) return OtpCheckResult.TooManyAttempts;
        return OtpCheckResult.Usable;
    }

    public void RecordFailure() => FailedAttempts++;

    public void Consume(DateTimeOffset now) => ConsumedAt = now;
}

public enum OtpCheckResult
{
    Usable,
    NotFound,
    Expired,
    TooManyAttempts
}
