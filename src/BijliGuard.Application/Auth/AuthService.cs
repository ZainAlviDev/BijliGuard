using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Common;
using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Common;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace BijliGuard.Application.Auth;

/// <summary>
/// Use cases for registration, email OTP verification, login, refresh-token rotation and password reset.
/// Every outcome is logged with the event names defined in the technical design (section 11.3).
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string AuditRegistered = "Auth.Registered";
    private const string AuditEmailVerified = "Auth.EmailVerified";
    private const string AuditLoggedIn = "Auth.LoginSucceeded";
    private const string AuditLoggedOut = "Auth.LoggedOut";
    private const string AuditTokenReused = "Auth.RefreshTokenReused";
    private const string AuditPasswordReset = "Auth.PasswordReset";

    private readonly IUserAccounts _users;
    private readonly IEmailOtpStore _otps;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IAuditLog _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOtpService _otpService;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly IValidator<RegisterCommand> _registerValidator;
    private readonly IValidator<VerifyEmailCommand> _verifyValidator;
    private readonly IValidator<ResendOtpCommand> _resendValidator;
    private readonly IValidator<LoginCommand> _loginValidator;
    private readonly IValidator<ForgotPasswordCommand> _forgotValidator;
    private readonly IValidator<ResetPasswordCommand> _resetValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserAccounts users,
        IEmailOtpStore otps,
        IRefreshTokenStore refreshTokens,
        IAuditLog audit,
        IUnitOfWork unitOfWork,
        IOtpService otpService,
        ITokenService tokens,
        IEmailSender email,
        IClock clock,
        ICorrelationIdAccessor correlation,
        IValidator<RegisterCommand> registerValidator,
        IValidator<VerifyEmailCommand> verifyValidator,
        IValidator<ResendOtpCommand> resendValidator,
        IValidator<LoginCommand> loginValidator,
        IValidator<ForgotPasswordCommand> forgotValidator,
        IValidator<ResetPasswordCommand> resetValidator,
        ILogger<AuthService> logger)
    {
        _users = users;
        _otps = otps;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _otpService = otpService;
        _tokens = tokens;
        _email = email;
        _clock = clock;
        _correlation = correlation;
        _registerValidator = registerValidator;
        _verifyValidator = verifyValidator;
        _resendValidator = resendValidator;
        _loginValidator = loginValidator;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _logger = logger;
    }

    public async Task<Result<RegistrationResult>> RegisterAsync(RegisterCommand command, CancellationToken ct)
    {
        var invalid = Validate(_registerValidator, command);
        if (invalid is not null) return Result<RegistrationResult>.Failure(invalid.Error!.Code, invalid.Error.Message, invalid.Error.Fields);

        var emailHash = LogSafe.Hash(command.Email);
        var existing = await _users.FindByEmailAsync(command.Email, ct);

        if (existing is not null && existing.IsEmailConfirmed)
        {
            _logger.LogWarning("Auth.RegistrationDuplicate {EmailHash}", emailHash);
            return Result<RegistrationResult>.Failure(ErrorCodes.EmailAlreadyRegistered, "An account with this email already exists.");
        }

        if (existing is not null)
        {
            // Unverified account: resend the code, keep the original password.
            _logger.LogInformation("Auth.RegistrationResent {EmailHash}", emailHash);
            return await IssueCodeAsync(existing, OtpPurpose.EmailVerification, ct);
        }

        var userId = await _users.CreateAsync(command.Email, command.Password, command.FullName, ct);
        if (userId is null)
        {
            _logger.LogWarning("Auth.RegistrationDuplicate {EmailHash}", emailHash);
            return Result<RegistrationResult>.Failure(ErrorCodes.EmailAlreadyRegistered, "An account with this email already exists.");
        }

        var user = new UserAccount(userId.Value, command.Email, command.FullName, IsEmailConfirmed: false);
        await RecordAuditAsync(user.Id, AuditRegistered, ct);
        _logger.LogInformation("Auth.Registered {UserIdHash} {EmailHash}", LogSafe.Hash(user.Id.ToString()), emailHash);

        return await IssueCodeAsync(user, OtpPurpose.EmailVerification, ct);
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailCommand command, CancellationToken ct)
    {
        var invalid = Validate(_verifyValidator, command);
        if (invalid is not null) return invalid;

        var user = await _users.FindByEmailAsync(command.Email, ct);
        if (user is null)
        {
            // Same answer as a wrong code, so the endpoint does not reveal accounts.
            _logger.LogWarning("Auth.OtpFailed {EmailHash} Reason {Reason}", LogSafe.Hash(command.Email), "unknown_email");
            return Result.Failure(ErrorCodes.OtpInvalid, "The code is incorrect.");
        }

        if (user.IsEmailConfirmed) return Result.Success();

        var codeFailure = await CheckCodeAsync(user, OtpPurpose.EmailVerification, command.Code, ct);
        if (codeFailure is not null) return codeFailure;

        await _users.MarkEmailConfirmedAsync(user.Id, ct);
        await RecordAuditAsync(user.Id, AuditEmailVerified, ct);
        _logger.LogInformation("Auth.EmailVerified {UserIdHash}", LogSafe.Hash(user.Id.ToString()));

        return Result.Success();
    }

    public async Task<Result> ResendOtpAsync(ResendOtpCommand command, CancellationToken ct)
    {
        var invalid = Validate(_resendValidator, command);
        if (invalid is not null) return invalid;

        var user = await _users.FindByEmailAsync(command.Email, ct);

        // Same response whether or not the account exists.
        if (user is null || user.IsEmailConfirmed)
        {
            _logger.LogInformation("Auth.OtpResendIgnored {EmailHash}", LogSafe.Hash(command.Email));
            return Result.Success();
        }

        var issued = await IssueCodeAsync(user, OtpPurpose.EmailVerification, ct);
        return issued.IsSuccess ? Result.Success() : Result.Failure(issued.Error!.Code, issued.Error.Message);
    }

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordCommand command, CancellationToken ct)
    {
        var invalid = Validate(_forgotValidator, command);
        if (invalid is not null) return invalid;

        var user = await _users.FindByEmailAsync(command.Email, ct);
        if (user is null)
        {
            _logger.LogInformation("Auth.PasswordResetIgnored {EmailHash} Reason {Reason}", LogSafe.Hash(command.Email), "unknown_email");
            return Result.Success();
        }

        var issued = await IssueCodeAsync(user, OtpPurpose.PasswordReset, ct);
        if (issued.IsSuccess)
        {
            _logger.LogInformation("Auth.PasswordResetRequested {UserIdHash}", LogSafe.Hash(user.Id.ToString()));
            return Result.Success();
        }

        // A cooldown means a code was just sent. Report success so the response looks the same as a new request.
        if (issued.Error!.Code == ErrorCodes.OtpResendCooldown)
        {
            _logger.LogInformation("Auth.PasswordResetIgnored {UserIdHash} Reason {Reason}", LogSafe.Hash(user.Id.ToString()), "cooldown");
            return Result.Success();
        }

        return Result.Failure(issued.Error.Code, issued.Error.Message);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        var invalid = Validate(_resetValidator, command);
        if (invalid is not null) return invalid;

        var user = await _users.FindByEmailAsync(command.Email, ct);
        if (user is null)
        {
            _logger.LogWarning("Auth.PasswordResetFailed {EmailHash} Reason {Reason}", LogSafe.Hash(command.Email), "unknown_email");
            return Result.Failure(ErrorCodes.OtpInvalid, "The code is incorrect.");
        }

        var codeFailure = await CheckCodeAsync(user, OtpPurpose.PasswordReset, command.Code, ct);
        if (codeFailure is not null) return codeFailure;

        await _users.SetPasswordAsync(user.Id, command.NewPassword, ct);

        // Receiving the code proves control of the mailbox, so the email is confirmed too.
        if (!user.IsEmailConfirmed) await _users.MarkEmailConfirmedAsync(user.Id, ct);

        // A password change ends every existing session, including any a stolen password may have created.
        var sessions = await _refreshTokens.FindByUserAsync(user.Id, ct);
        foreach (var session in sessions) session.Revoke(_clock.UtcNow);

        await RecordAuditAsync(user.Id, AuditPasswordReset, ct);
        _logger.LogInformation("Auth.PasswordReset {UserIdHash} SessionsRevoked {SessionCount}",
            LogSafe.Hash(user.Id.ToString()), sessions.Count);

        return Result.Success();
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        var invalid = Validate(_loginValidator, command);
        if (invalid is not null) return Result<AuthResponse>.Failure(invalid.Error!.Code, invalid.Error.Message, invalid.Error.Fields);

        var emailHash = LogSafe.Hash(command.Email);
        var user = await _users.FindByEmailAsync(command.Email, ct);
        if (user is null)
        {
            _logger.LogWarning("Auth.LoginFailed {EmailHash} Reason {Reason}", emailHash, "unknown_email");
            return Result<AuthResponse>.Failure(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        var userHash = LogSafe.Hash(user.Id.ToString());
        var check = await _users.CheckPasswordAsync(user.Id, command.Password, ct);
        if (check == PasswordCheckResult.LockedOut)
        {
            _logger.LogWarning("Auth.LoginLocked {UserIdHash}", userHash);
            return Result<AuthResponse>.Failure(ErrorCodes.AccountLocked, "Too many failed attempts. Try again later.");
        }

        if (check == PasswordCheckResult.Invalid)
        {
            _logger.LogWarning("Auth.LoginFailed {UserIdHash} Reason {Reason}", userHash, "wrong_password");
            return Result<AuthResponse>.Failure(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (!user.IsEmailConfirmed)
        {
            _logger.LogWarning("Auth.LoginBlocked {UserIdHash} Reason {Reason}", userHash, "email_not_verified");
            return Result<AuthResponse>.Failure(ErrorCodes.EmailNotVerified, "Verify your email before signing in.");
        }

        var (response, _) = await IssueSessionAsync(user, Guid.NewGuid(), ct);
        await RecordAuditAsync(user.Id, AuditLoggedIn, ct);
        _logger.LogInformation("Auth.LoginSucceeded {UserIdHash}", userHash);

        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Result<AuthResponse>.Failure(ErrorCodes.TokenInvalid, "Session is no longer valid. Sign in again.");

        var now = _clock.UtcNow;
        var stored = await _refreshTokens.FindByHashAsync(_tokens.HashRefreshToken(refreshToken), ct);
        if (stored is null)
            return Result<AuthResponse>.Failure(ErrorCodes.TokenInvalid, "Session is no longer valid. Sign in again.");

        if (stored.IsRevoked)
        {
            // A rotated token was presented again: treat the whole family as compromised.
            var family = await _refreshTokens.FindFamilyAsync(stored.FamilyId, ct);
            foreach (var token in family) token.Revoke(now);
            await RecordAuditAsync(stored.UserId, AuditTokenReused, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning("Auth.RefreshTokenReused {UserIdHash} {FamilyIdHash}",
                LogSafe.Hash(stored.UserId.ToString()), LogSafe.Hash(stored.FamilyId.ToString()));
            return Result<AuthResponse>.Failure(ErrorCodes.TokenReused, "Session was closed for security. Sign in again.");
        }

        if (!stored.IsActive(now))
            return Result<AuthResponse>.Failure(ErrorCodes.TokenInvalid, "Session has expired. Sign in again.");

        var user = await _users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsEmailConfirmed)
            return Result<AuthResponse>.Failure(ErrorCodes.TokenInvalid, "Session is no longer valid. Sign in again.");

        var (response, newHash) = await IssueSessionAsync(user, stored.FamilyId, ct);
        stored.Revoke(now, newHash);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogDebug("Auth.RefreshRotated {UserIdHash}", LogSafe.Hash(user.Id.ToString()));

        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken ct)
    {
        // Logout is idempotent: an unknown or already-revoked token still ends in a signed-out state.
        if (string.IsNullOrWhiteSpace(refreshToken)) return Result.Success();

        var stored = await _refreshTokens.FindByHashAsync(_tokens.HashRefreshToken(refreshToken), ct);
        if (stored is not null && stored.IsActive(_clock.UtcNow))
        {
            stored.Revoke(_clock.UtcNow);
            await RecordAuditAsync(stored.UserId, AuditLoggedOut, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Auth.LoggedOut {UserIdHash}", LogSafe.Hash(stored.UserId.ToString()));
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks a submitted code for one purpose. Returns null when the code is valid, and consumes it.
    /// Failed attempts are counted, and the code locks after too many of them.
    /// </summary>
    private async Task<Result?> CheckCodeAsync(UserAccount user, OtpPurpose purpose, string code, CancellationToken ct)
    {
        var userHash = LogSafe.Hash(user.Id.ToString());
        var otp = await _otps.GetLatestAsync(user.Id, purpose, ct);
        if (otp is null)
        {
            _logger.LogWarning("Auth.OtpFailed {UserIdHash} Purpose {Purpose} Reason {Reason}", userHash, purpose, "no_code");
            return Result.Failure(ErrorCodes.OtpNotFound, "No active code. Request a new one.");
        }

        var now = _clock.UtcNow;
        switch (otp.Check(now))
        {
            case OtpCheckResult.NotFound:
                _logger.LogWarning("Auth.OtpFailed {UserIdHash} Purpose {Purpose} Reason {Reason}", userHash, purpose, "already_used");
                return Result.Failure(ErrorCodes.OtpNotFound, "No active code. Request a new one.");
            case OtpCheckResult.Expired:
                _logger.LogInformation("Auth.OtpExpired {UserIdHash} Purpose {Purpose}", userHash, purpose);
                return Result.Failure(ErrorCodes.OtpExpired, "The code has expired. Request a new one.");
            case OtpCheckResult.TooManyAttempts:
                _logger.LogWarning("Auth.OtpLocked {UserIdHash} Purpose {Purpose}", userHash, purpose);
                return Result.Failure(ErrorCodes.OtpTooManyAttempts, "Too many incorrect attempts. Request a new code.");
        }

        if (!_otpService.Matches(user.Id, purpose, code, otp.CodeHash))
        {
            otp.RecordFailure();
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning("Auth.OtpFailed {UserIdHash} Purpose {Purpose} Reason {Reason} Attempts {Attempts}",
                userHash, purpose, "wrong_code", otp.FailedAttempts);
            return Result.Failure(ErrorCodes.OtpInvalid, "The code is incorrect.");
        }

        otp.Consume(now);
        await _unitOfWork.SaveChangesAsync(ct);
        return null;
    }

    private async Task<Result<RegistrationResult>> IssueCodeAsync(UserAccount user, OtpPurpose purpose, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var userHash = LogSafe.Hash(user.Id.ToString());
        var last = await _otps.GetLatestAsync(user.Id, purpose, ct);

        if (!OtpPolicy.CanResend(last?.IssuedAt, now))
        {
            _logger.LogInformation("Auth.OtpResendCooldown {UserIdHash} Purpose {Purpose}", userHash, purpose);
            return Result<RegistrationResult>.Failure(ErrorCodes.OtpResendCooldown,
                $"Wait {OtpPolicy.ResendCooldown.TotalSeconds} seconds before requesting another code.");
        }

        var code = _otpService.GenerateCode();
        var otp = EmailOtp.Issue(user.Id, purpose, _otpService.HashCode(user.Id, purpose, code), now);

        await _otps.ReplaceAsync(otp, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            await _email.SendOtpAsync(user.Email, code, OtpPolicy.Lifetime, purpose, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Auth.OtpDeliveryFailed {UserIdHash} Purpose {Purpose}", userHash, purpose);
            return Result<RegistrationResult>.Failure(ErrorCodes.EmailDeliveryFailed,
                "We could not send the code right now. Please try again shortly.");
        }

        _logger.LogInformation("Auth.OtpIssued {UserIdHash} Purpose {Purpose}", userHash, purpose);
        return Result<RegistrationResult>.Success(new RegistrationResult(user.Email, otp.ExpiresAt));
    }

    private async Task<(AuthResponse Response, string RefreshHash)> IssueSessionAsync(UserAccount user, Guid familyId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var roles = await _users.GetRolesAsync(user.Id, ct);
        var (accessToken, accessExpiresAt) = _tokens.IssueAccessToken(user, roles);

        var rawRefresh = _tokens.GenerateRefreshToken();
        var refreshHash = _tokens.HashRefreshToken(rawRefresh);
        var refresh = RefreshToken.Issue(user.Id, familyId, refreshHash, now, _tokens.RefreshTokenLifetime);
        await _refreshTokens.AddAsync(refresh, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var response = new AuthResponse(
            accessToken,
            accessExpiresAt,
            rawRefresh,
            refresh.ExpiresAt,
            new UserDto(user.Id, user.Email, user.FullName));

        return (response, refreshHash);
    }

    private async Task RecordAuditAsync(Guid actorUserId, string action, CancellationToken ct)
    {
        await _audit.RecordAsync(
            AuditEntry.Create(actorUserId, action, _clock.UtcNow, _correlation.CorrelationId),
            ct);
        // Persist immediately: an audit entry that is not saved is lost when the request ends.
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static Result? Validate<T>(IValidator<T> validator, T command)
    {
        var validation = validator.Validate(command);
        if (validation.IsValid) return null;

        var fields = validation.Errors
            .Select(e => new FieldError(e.PropertyName, e.ErrorMessage))
            .ToList();
        return Result.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.", fields);
    }
}
