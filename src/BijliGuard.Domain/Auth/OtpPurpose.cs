namespace BijliGuard.Domain.Auth;

/// <summary>What a one-time code is allowed to authorize. A code issued for one purpose is never valid for another.</summary>
public enum OtpPurpose
{
    EmailVerification = 1,
    PasswordReset = 2
}
