using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Common;
using BijliGuard.Domain.Common;
using BijliGuard.Domain.Consent;
using Microsoft.Extensions.Logging;

namespace BijliGuard.Application.Consent;

public sealed record ConsentStatusDto(string CurrentVersion, string? AcceptedVersion, DateTimeOffset? AcceptedAt, bool IsCurrent);

public interface IConsentService
{
    Task<Result<ConsentStatusDto>> GetAsync(Guid userId, CancellationToken ct);

    Task<Result<ConsentStatusDto>> AcceptAsync(Guid userId, string version, CancellationToken ct);
}

public interface IConsentRepository
{
    Task<ConsentRecord?> GetLatestAsync(Guid userId, CancellationToken ct);

    Task AddAsync(ConsentRecord record, CancellationToken ct);
}

/// <summary>
/// Records that the user accepted the current consent text. The version is what was shown, so a changed
/// text asks everyone again.
/// </summary>
public sealed class ConsentService(
    IConsentRepository repo,
    IUnitOfWork unitOfWork,
    IAuditLog audit,
    IClock clock,
    ICorrelationIdAccessor correlation,
    ILogger<ConsentService> logger) : IConsentService
{
    /// <summary>The consent text the apps show. Change this when the text changes, so users are asked again.</summary>
    public const string CurrentVersion = "2026-11-15";

    public async Task<Result<ConsentStatusDto>> GetAsync(Guid userId, CancellationToken ct)
    {
        var latest = await repo.GetLatestAsync(userId, ct);
        return Result<ConsentStatusDto>.Success(ToDto(latest));
    }

    public async Task<Result<ConsentStatusDto>> AcceptAsync(Guid userId, string version, CancellationToken ct)
    {
        if (version != CurrentVersion)
            return Result<ConsentStatusDto>.Failure(ErrorCodes.ConsentVersionMismatch,
                "The consent text has changed. Read the latest version and accept again.");

        var latest = await repo.GetLatestAsync(userId, ct);
        if (latest is { Version: CurrentVersion }) return Result<ConsentStatusDto>.Success(ToDto(latest));

        var record = ConsentRecord.Create(userId, CurrentVersion, clock.UtcNow);
        await repo.AddAsync(record, ct);
        await audit.RecordAsync(AuditEntry.Create(userId, "Consent.Accepted", clock.UtcNow, correlation.CorrelationId), ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Consent.Accepted {Version}", CurrentVersion);

        return Result<ConsentStatusDto>.Success(ToDto(record));
    }

    private static ConsentStatusDto ToDto(ConsentRecord? record) =>
        new(CurrentVersion, record?.Version, record?.AcceptedAt, record?.Version == CurrentVersion);
}
