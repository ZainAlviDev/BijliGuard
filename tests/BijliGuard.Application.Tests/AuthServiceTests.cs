using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Auth;
using BijliGuard.Application.Common;
using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Common;
using BijliGuard.Infrastructure.Security;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BijliGuard.Application.Tests;

public class AuthServiceTests
{
    private const string Email = "ayesha@example.com";
    private const string Password = "StrongPass123";

    private readonly Harness _h = new();

    [Fact]
    public async Task Register_creates_an_unverified_account_and_emails_a_six_digit_code()
    {
        var result = await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, "Ayesha"), default);

        result.IsSuccess.Should().BeTrue();
        _h.Users.Accounts.Should().ContainSingle(u => u.Email == Email && !u.IsEmailConfirmed);
        _h.Email.LastCode.Should().MatchRegex("^[0-9]{6}$");
        _h.Email.SendCount.Should().Be(1);
        result.Value!.OtpExpiresAt.Should().Be(_h.Clock.UtcNow.Add(OtpPolicy.Lifetime));
    }

    [Fact]
    public async Task Register_rejects_a_weak_password_with_field_details()
    {
        var result = await _h.Auth.RegisterAsync(new RegisterCommand(Email, "short", null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationFailed);
        result.Error.Fields.Should().Contain(f => f.Field == "Password");
        _h.Users.Accounts.Should().BeEmpty();
        _h.Email.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Register_refuses_an_email_that_is_already_verified()
    {
        await RegisterAndVerifyAsync();

        var result = await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);

        result.Error!.Code.Should().Be(ErrorCodes.EmailAlreadyRegistered);
    }

    [Fact]
    public async Task Register_again_before_verifying_resends_the_code_without_changing_the_password()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);
        _h.Clock.Advance(TimeSpan.FromMinutes(2));

        var result = await _h.Auth.RegisterAsync(new RegisterCommand(Email, "AnotherPass999", null), default);

        result.IsSuccess.Should().BeTrue();
        _h.Users.Accounts.Should().ContainSingle();
        _h.Email.SendCount.Should().Be(2);
        (await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default)).Error!.Code
            .Should().Be(ErrorCodes.EmailNotVerified);
    }

    [Fact]
    public async Task Correct_code_verifies_the_email_and_cannot_be_reused()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);
        var code = _h.Email.LastCode!;

        var first = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, code), default);
        var second = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, code), default);

        first.IsSuccess.Should().BeTrue();
        _h.Users.Accounts.Should().ContainSingle(u => u.IsEmailConfirmed);
        second.IsSuccess.Should().BeTrue("verifying an already verified account is idempotent");
    }

    [Fact]
    public async Task Wrong_code_is_rejected_and_counted_until_the_code_locks()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);
        var wrong = WrongCodeFor(_h.Email.LastCode!);

        for (var i = 1; i < OtpPolicy.MaxFailedAttempts; i++)
        {
            var attempt = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, wrong), default);
            attempt.Error!.Code.Should().Be(ErrorCodes.OtpInvalid);
        }

        var last = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, wrong), default);
        last.Error!.Code.Should().Be(ErrorCodes.OtpInvalid);

        var locked = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, _h.Email.LastCode!), default);
        locked.Error!.Code.Should().Be(ErrorCodes.OtpTooManyAttempts,
            "even the correct code is refused after too many failed attempts");
    }

    [Fact]
    public async Task Expired_code_is_refused_with_a_clear_code()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);
        _h.Clock.Advance(OtpPolicy.Lifetime);

        var result = await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, _h.Email.LastCode!), default);

        result.Error!.Code.Should().Be(ErrorCodes.OtpExpired);
    }

    [Fact]
    public async Task Resend_inside_the_cooldown_is_refused()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);

        var result = await _h.Auth.ResendOtpAsync(new ResendOtpCommand(Email), default);

        result.Error!.Code.Should().Be(ErrorCodes.OtpResendCooldown);
        _h.Email.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task Resend_for_an_unknown_email_looks_identical_to_success()
    {
        var result = await _h.Auth.ResendOtpAsync(new ResendOtpCommand("nobody@example.com"), default);

        result.IsSuccess.Should().BeTrue();
        _h.Email.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Login_is_blocked_until_the_email_is_verified()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);

        var result = await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default);

        result.Error!.Code.Should().Be(ErrorCodes.EmailNotVerified);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_gives_the_same_error_as_an_unknown_email()
    {
        await RegisterAndVerifyAsync();

        var wrongPassword = await _h.Auth.LoginAsync(new LoginCommand(Email, "WrongPass123"), default);
        var unknown = await _h.Auth.LoginAsync(new LoginCommand("nobody@example.com", Password), default);

        wrongPassword.Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        unknown.Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        wrongPassword.Error.Message.Should().Be(unknown.Error.Message);
    }

    [Fact]
    public async Task Login_returns_a_jwt_access_token_and_a_refresh_token()
    {
        await RegisterAndVerifyAsync();

        var result = await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Split('.').Should().HaveCount(3, "a JWT has three segments");
        result.Value.AccessTokenExpiresAt.Should().Be(_h.Clock.UtcNow.AddMinutes(15));
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Value.User.Email.Should().Be(Email);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_is_then_refused_and_revokes_the_family()
    {
        await RegisterAndVerifyAsync();
        var first = (await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default)).Value!;

        var rotated = await _h.Auth.RefreshAsync(first.RefreshToken, default);
        rotated.IsSuccess.Should().BeTrue();
        rotated.Value!.RefreshToken.Should().NotBe(first.RefreshToken);

        var reused = await _h.Auth.RefreshAsync(first.RefreshToken, default);
        reused.Error!.Code.Should().Be(ErrorCodes.TokenReused);

        var newest = await _h.Auth.RefreshAsync(rotated.Value.RefreshToken, default);
        newest.Error!.Code.Should().Be(ErrorCodes.TokenReused,
            "reuse of a rotated token must revoke the whole family, including the newest token");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        await RegisterAndVerifyAsync();
        var session = (await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default)).Value!;

        (await _h.Auth.LogoutAsync(session.RefreshToken, default)).IsSuccess.Should().BeTrue();
        (await _h.Auth.LogoutAsync(session.RefreshToken, default)).IsSuccess.Should().BeTrue();

        var refresh = await _h.Auth.RefreshAsync(session.RefreshToken, default);
        refresh.Error!.Code.Should().Be(ErrorCodes.TokenReused);
    }

    [Fact]
    public async Task Successful_actions_are_written_to_the_audit_log()
    {
        await RegisterAndVerifyAsync();
        await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default);

        _h.Audit.Entries.Select(e => e.Action).Should()
            .Contain(new[] { "Auth.Registered", "Auth.EmailVerified", "Auth.LoginSucceeded" });
    }

    [Fact]
    public async Task Forgot_password_sends_a_reset_code_that_is_not_valid_for_email_verification()
    {
        await RegisterAndVerifyAsync();
        var sentBefore = _h.Email.SendCount;

        var result = await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);

        result.IsSuccess.Should().BeTrue();
        _h.Email.SendCount.Should().Be(sentBefore + 1);
        _h.Email.LastPurpose.Should().Be(OtpPurpose.PasswordReset);
        _h.Otps.All.Should().Contain(o => o.Purpose == OtpPurpose.PasswordReset);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_looks_identical_and_sends_nothing()
    {
        var result = await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand("nobody@example.com"), default);

        result.IsSuccess.Should().BeTrue();
        _h.Email.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Forgot_password_inside_the_cooldown_still_reports_success()
    {
        await RegisterAndVerifyAsync();
        await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);

        var second = await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);

        second.IsSuccess.Should().BeTrue("the response must not reveal that a code was just sent");
        _h.Email.SendCount.Should().Be(2, "no third email is sent inside the cooldown");
    }

    [Fact]
    public async Task Reset_with_the_correct_code_sets_the_new_password_and_ends_old_sessions()
    {
        await RegisterAndVerifyAsync();
        var session = (await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default)).Value!;
        await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);

        var reset = await _h.Auth.ResetPasswordAsync(
            new ResetPasswordCommand(Email, _h.Email.LastCode!, "BrandNewPass999"), default);

        reset.IsSuccess.Should().BeTrue();
        _h.Users.Store.Single().Password.Should().Be("BrandNewPass999");
        (await _h.Auth.LoginAsync(new LoginCommand(Email, Password), default)).Error!.Code
            .Should().Be(ErrorCodes.InvalidCredentials, "the old password no longer works");
        (await _h.Auth.LoginAsync(new LoginCommand(Email, "BrandNewPass999"), default)).IsSuccess
            .Should().BeTrue();
        (await _h.Auth.RefreshAsync(session.RefreshToken, default)).Error!.Code
            .Should().Be(ErrorCodes.TokenReused, "sessions from before the reset are revoked");
    }

    [Fact]
    public async Task Reset_code_cannot_be_reused()
    {
        await RegisterAndVerifyAsync();
        await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);
        var code = _h.Email.LastCode!;
        await _h.Auth.ResetPasswordAsync(new ResetPasswordCommand(Email, code, "BrandNewPass999"), default);

        var again = await _h.Auth.ResetPasswordAsync(new ResetPasswordCommand(Email, code, "AnotherPass999"), default);

        again.Error!.Code.Should().Be(ErrorCodes.OtpNotFound);
    }

    [Fact]
    public async Task An_email_verification_code_cannot_be_used_to_reset_the_password()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, null), default);
        var verificationCode = _h.Email.LastCode!;

        var reset = await _h.Auth.ResetPasswordAsync(
            new ResetPasswordCommand(Email, verificationCode, "BrandNewPass999"), default);

        reset.Error!.Code.Should().Be(ErrorCodes.OtpNotFound, "codes are bound to their purpose");
    }

    [Fact]
    public async Task Reset_rejects_a_weak_new_password_before_checking_the_code()
    {
        await RegisterAndVerifyAsync();
        await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);

        var reset = await _h.Auth.ResetPasswordAsync(
            new ResetPasswordCommand(Email, _h.Email.LastCode!, "short"), default);

        reset.Error!.Code.Should().Be(ErrorCodes.ValidationFailed);
        reset.Error.Fields.Should().Contain(f => f.Field == "NewPassword");
    }

    [Fact]
    public async Task Reset_code_locks_after_five_wrong_attempts()
    {
        await RegisterAndVerifyAsync();
        await _h.Auth.ForgotPasswordAsync(new ForgotPasswordCommand(Email), default);
        var wrong = WrongCodeFor(_h.Email.LastCode!);

        for (var i = 0; i < OtpPolicy.MaxFailedAttempts; i++)
            await _h.Auth.ResetPasswordAsync(new ResetPasswordCommand(Email, wrong, "BrandNewPass999"), default);

        var locked = await _h.Auth.ResetPasswordAsync(
            new ResetPasswordCommand(Email, _h.Email.LastCode!, "BrandNewPass999"), default);
        locked.Error!.Code.Should().Be(ErrorCodes.OtpTooManyAttempts);
    }

    private async Task RegisterAndVerifyAsync()
    {
        await _h.Auth.RegisterAsync(new RegisterCommand(Email, Password, "Ayesha"), default);
        await _h.Auth.VerifyEmailAsync(new VerifyEmailCommand(Email, _h.Email.LastCode!), default);
    }

    private static string WrongCodeFor(string code) => code == "000000" ? "111111" : "000000";

    /// <summary>Wires the real AuthService to in-memory fakes so the rules run without a database.</summary>
    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();
        public FakeUsers Users { get; } = new();
        public FakeOtpStore Otps { get; } = new();
        public FakeRefreshStore Refresh { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeEmail Email { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public OtpService OtpService { get; } = new(Options.Create(new OtpOptions
        {
            Secret = "test-secret-that-is-long-enough-for-hmac"
        }));

        public AuthService Auth { get; }

        public Harness()
        {
            Auth = new AuthService(
                Users, Otps, Refresh, Audit, new FakeUnitOfWork(), OtpService, Tokens, Email, Clock,
                new FakeCorrelation(),
                new RegisterCommandValidator(),
                new VerifyEmailCommandValidator(),
                new ResendOtpCommandValidator(),
                new LoginCommandValidator(),
                new ForgotPasswordCommandValidator(),
                new ResetPasswordCommandValidator(),
                NullLogger<AuthService>.Instance);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed class FakeUsers : IUserAccounts
    {
        public List<(UserAccount Account, string Password)> Store { get; } = [];

        public IEnumerable<UserAccount> Accounts => Store.Select(s => s.Account);

        public Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(Store.Select(s => s.Account)
                .FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

        public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Store.Select(s => s.Account).FirstOrDefault(u => u.Id == userId));

        public Task<Guid?> CreateAsync(string email, string password, string? fullName, CancellationToken ct)
        {
            if (Store.Any(s => s.Account.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<Guid?>(null);
            var id = Guid.NewGuid();
            Store.Add((new UserAccount(id, email, fullName, false), password));
            return Task.FromResult<Guid?>(id);
        }

        public Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken ct)
        {
            var match = Store.FirstOrDefault(s => s.Account.Id == userId);
            return Task.FromResult(match.Password == password ? PasswordCheckResult.Valid : PasswordCheckResult.Invalid);
        }

        public Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct)
        {
            var index = Store.FindIndex(s => s.Account.Id == userId);
            Store[index] = (Store[index].Account, newPassword);
            return Task.CompletedTask;
        }

        public Task MarkEmailConfirmedAsync(Guid userId, CancellationToken ct)
        {
            var index = Store.FindIndex(s => s.Account.Id == userId);
            var current = Store[index];
            Store[index] = (current.Account with { IsEmailConfirmed = true }, current.Password);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["User"]);
    }

    private sealed class FakeOtpStore : IEmailOtpStore
    {
        private readonly List<EmailOtp> _items = [];

        public IReadOnlyList<EmailOtp> All => _items;

        public Task<EmailOtp?> GetLatestAsync(Guid userId, OtpPurpose purpose, CancellationToken ct) =>
            Task.FromResult(_items.Where(x => x.UserId == userId && x.Purpose == purpose)
                .OrderByDescending(x => x.IssuedAt).FirstOrDefault());

        public Task ReplaceAsync(EmailOtp otp, CancellationToken ct)
        {
            _items.RemoveAll(x => x.UserId == otp.UserId && x.Purpose == otp.Purpose);
            _items.Add(otp);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRefreshStore : IRefreshTokenStore
    {
        public List<RefreshToken> Items { get; } = [];

        public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(x => x.TokenHash == tokenHash));

        public Task<IReadOnlyList<RefreshToken>> FindFamilyAsync(Guid familyId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(x => x.FamilyId == familyId).ToList());

        public Task<IReadOnlyList<RefreshToken>> FindByUserAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(x => x.UserId == userId).ToList());

        public Task AddAsync(RefreshToken token, CancellationToken ct)
        {
            Items.Add(token);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> ListForUserAsync(Guid userId, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Entries.Where(e => e.ActorUserId == userId).Take(count).ToList());
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeEmail : IEmailSender
    {
        public string? LastCode { get; private set; }
        public OtpPurpose? LastPurpose { get; private set; }
        public int SendCount { get; private set; }

        public Task SendOtpAsync(string toEmail, string code, TimeSpan validFor, OtpPurpose purpose, CancellationToken ct)
        {
            LastCode = code;
            LastPurpose = purpose;
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokens : ITokenService
    {
        private readonly DateTimeOffset _issuedAt = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

        public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(7);

        public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(UserAccount user, IReadOnlyList<string> roles) =>
            ($"{Guid.NewGuid():N}.{Guid.NewGuid():N}.sig", _issuedAt.AddMinutes(15));

        public string GenerateRefreshToken() => Guid.NewGuid().ToString("N");

        public string HashRefreshToken(string token) => "hash:" + token;
    }

    private sealed class FakeCorrelation : ICorrelationIdAccessor
    {
        public string? CorrelationId => "test-correlation-id";
    }
}
