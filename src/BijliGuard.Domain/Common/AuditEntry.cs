namespace BijliGuard.Domain.Common;

/// <summary>Append-only record of a business action. Never edited after creation.</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? Details { get; private set; }

    public static AuditEntry Create(Guid? actorUserId, string action, DateTimeOffset occurredAt, string? correlationId, string? details = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            Details = details
        };
}
