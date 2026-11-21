namespace BijliGuard.Domain.Consent;

/// <summary>
/// The user accepted a consent text version. Recorded once per version; the version is what was shown.
/// </summary>
public sealed class ConsentRecord
{
    private ConsentRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; private set; }

    public static ConsentRecord Create(Guid userId, string version, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Version = version,
            AcceptedAt = now
        };
}
