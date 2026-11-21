using BijliGuard.Domain.Auth;

namespace BijliGuard.Application.Abstractions;

public interface IOtpService
{
    string GenerateCode();

    string HashCode(Guid userId, OtpPurpose purpose, string code);

    bool Matches(Guid userId, OtpPurpose purpose, string code, string storedHash);
}

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(UserAccount user, IReadOnlyList<string> roles);

    string GenerateRefreshToken();

    string HashRefreshToken(string token);

    TimeSpan RefreshTokenLifetime { get; }
}

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string code, TimeSpan validFor, OtpPurpose purpose, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
