namespace BijliGuard.Application.Abstractions;

public sealed record UserAccount(Guid Id, string Email, string? FullName, bool IsEmailConfirmed);

public enum PasswordCheckResult
{
    Valid,
    Invalid,
    LockedOut
}

public interface IUserAccounts
{
    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Returns the new user id, or null when the email is already registered.</summary>
    Task<Guid?> CreateAsync(string email, string password, string? fullName, CancellationToken ct);

    Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken ct);

    /// <summary>Replaces the password. Identity's password rules are applied.</summary>
    Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct);

    Task MarkEmailConfirmedAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct);
}
