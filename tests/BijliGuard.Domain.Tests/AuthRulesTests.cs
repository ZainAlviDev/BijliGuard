using BijliGuard.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace BijliGuard.Domain.Tests;

public class OtpPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resend_is_allowed_when_no_code_was_issued()
    {
        OtpPolicy.CanResend(null, Now).Should().BeTrue();
    }

    [Fact]
    public void Resend_is_blocked_inside_the_cooldown_window()
    {
        OtpPolicy.CanResend(Now, Now.AddSeconds(59)).Should().BeFalse();
    }

    [Fact]
    public void Resend_is_allowed_exactly_at_the_cooldown_boundary()
    {
        OtpPolicy.CanResend(Now, Now.AddSeconds(60)).Should().BeTrue();
    }
}

public class EmailOtpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private static EmailOtp Issue() =>
        EmailOtp.Issue(UserId, OtpPurpose.EmailVerification, "hash", Now);

    [Fact]
    public void A_new_code_is_usable_and_expires_after_ten_minutes()
    {
        var otp = Issue();

        otp.ExpiresAt.Should().Be(Now.Add(OtpPolicy.Lifetime));
        otp.Check(Now.AddMinutes(9)).Should().Be(OtpCheckResult.Usable);
    }

    [Fact]
    public void A_code_is_expired_at_and_after_its_expiry_time()
    {
        var otp = Issue();

        otp.Check(otp.ExpiresAt).Should().Be(OtpCheckResult.Expired);
    }

    [Fact]
    public void A_code_is_locked_after_five_failed_attempts()
    {
        var otp = Issue();

        for (var i = 0; i < OtpPolicy.MaxFailedAttempts; i++) otp.RecordFailure();

        otp.Check(Now).Should().Be(OtpCheckResult.TooManyAttempts);
    }

    [Fact]
    public void A_consumed_code_cannot_be_used_again()
    {
        var otp = Issue();

        otp.Consume(Now);

        otp.Check(Now).Should().Be(OtpCheckResult.NotFound);
    }
}

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_token_is_active_until_it_expires()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h1", Now, TimeSpan.FromDays(7));

        token.IsActive(Now.AddDays(6)).Should().BeTrue();
        token.IsActive(Now.AddDays(7)).Should().BeFalse();
    }

    [Fact]
    public void Revoking_keeps_the_first_revocation_time_and_replacement()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h1", Now, TimeSpan.FromDays(7));

        token.Revoke(Now.AddMinutes(1), "h2");
        token.Revoke(Now.AddMinutes(5), "h3");

        token.IsRevoked.Should().BeTrue();
        token.RevokedAt.Should().Be(Now.AddMinutes(1));
        token.ReplacedByTokenHash.Should().Be("h2");
        token.IsActive(Now.AddMinutes(2)).Should().BeFalse();
    }
}
