using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Common;

namespace BijliGuard.Application.Activity;

public sealed record ActivityItemDto(string Action, DateTimeOffset OccurredAt);

public interface IActivityService
{
    Task<Result<IReadOnlyList<ActivityItemDto>>> ListMineAsync(Guid userId, CancellationToken ct);
}

/// <summary>The caller's own audit entries. Only actions, never values or identifiers of other people.</summary>
public sealed class ActivityService(IAuditLog audit) : IActivityService
{
    private const int MaxItems = 50;

    public async Task<Result<IReadOnlyList<ActivityItemDto>>> ListMineAsync(Guid userId, CancellationToken ct)
    {
        var entries = await audit.ListForUserAsync(userId, MaxItems, ct);
        return Result<IReadOnlyList<ActivityItemDto>>.Success(
            entries.Select(e => new ActivityItemDto(e.Action, e.OccurredAt)).ToList());
    }
}
