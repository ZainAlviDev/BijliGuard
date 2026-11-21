namespace BijliGuard.Domain.Billing;

/// <summary>Pakistani electricity distribution companies supported by the app.</summary>
public enum Disco
{
    Lesco,
    Iesco,
    Mepco,
    Fesco,
    Gepco,
    Hesco,
    Sepco,
    Pesco,
    Qesco,
    KElectric
}

/// <summary>
/// The household owned by one user. The consumer reference is stored encrypted; only a masked form is ever returned.
/// </summary>
public sealed class Household
{
    public const int DefaultTargetUnits = 200;

    private Household()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Disco Provider { get; private set; }
    public string ConsumerReferenceProtected { get; private set; } = string.Empty;
    public string ConsumerReferenceMasked { get; private set; } = string.Empty;
    public int TargetUnits { get; private set; } = DefaultTargetUnits;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Household Create(Guid ownerUserId, Disco provider, string protectedReference, string maskedReference, int targetUnits, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Provider = provider,
            ConsumerReferenceProtected = protectedReference,
            ConsumerReferenceMasked = maskedReference,
            TargetUnits = targetUnits,
            CreatedAt = now,
            UpdatedAt = now
        };

    public void Update(Disco provider, string protectedReference, string maskedReference, int targetUnits, DateTimeOffset now)
    {
        Provider = provider;
        ConsumerReferenceProtected = protectedReference;
        ConsumerReferenceMasked = maskedReference;
        TargetUnits = targetUnits;
        UpdatedAt = now;
    }

    public const int MinTargetUnits = 50;
    public const int MaxTargetUnits = 2000;

    public void ChangeTarget(int targetUnits, DateTimeOffset now)
    {
        TargetUnits = targetUnits;
        UpdatedAt = now;
    }

    /// <summary>Shows only the last four characters, for example "******1234".</summary>
    public static string Mask(string reference)
    {
        var trimmed = reference.Trim();
        return trimmed.Length <= 4 ? new string('*', trimmed.Length) : new string('*', trimmed.Length - 4) + trimmed[^4..];
    }
}
