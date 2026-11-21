namespace BijliGuard.Application.Auth;

public sealed record RegisterCommand(string Email, string Password, string? FullName);

public sealed record VerifyEmailCommand(string Email, string Code);

public sealed record ResendOtpCommand(string Email);

public sealed record LoginCommand(string Email, string Password);

public sealed record ForgotPasswordCommand(string Email);

public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword);

public sealed record RegistrationResult(string Email, DateTimeOffset OtpExpiresAt);

public sealed record UserDto(Guid Id, string Email, string? FullName);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);
