namespace BijliGuard.Domain.Auth;

/// <summary>
/// A refresh token is stored only as a hash. Tokens belong to a family; rotating a token
/// keeps the family. Presenting a revoked token signals theft and revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            IssuedAt = now,
            ExpiresAt = now + lifetime
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public bool IsRevoked => RevokedAt is not null;

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash = null)
    {
        RevokedAt ??= now;
        ReplacedByTokenHash ??= replacedByTokenHash;
    }
}
