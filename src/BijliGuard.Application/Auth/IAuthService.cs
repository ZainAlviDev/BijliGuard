using BijliGuard.Application.Common;

namespace BijliGuard.Application.Auth;

public interface IAuthService
{
    Task<Result<RegistrationResult>> RegisterAsync(RegisterCommand command, CancellationToken ct);

    Task<Result> VerifyEmailAsync(VerifyEmailCommand command, CancellationToken ct);

    Task<Result> ResendOtpAsync(ResendOtpCommand command, CancellationToken ct);

    Task<Result<AuthResponse>> LoginAsync(LoginCommand command, CancellationToken ct);

    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<Result> LogoutAsync(string refreshToken, CancellationToken ct);

    /// <summary>Always reports success for unknown emails, so the endpoint cannot be used to find accounts.</summary>
    Task<Result> ForgotPasswordAsync(ForgotPasswordCommand command, CancellationToken ct);

    Task<Result> ResetPasswordAsync(ResetPasswordCommand command, CancellationToken ct);
}
