using System.Net.Mail;
using BijliGuard.Domain.Auth;
using FluentValidation;

namespace BijliGuard.Application.Auth;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(256)
            .Must(EmailRules.IsValid).WithMessage("Enter a valid email address.");

        RuleFor(x => x.Password).StrongPassword();

        RuleFor(x => x.FullName).MaximumLength(120);
    }
}

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .Must(EmailRules.IsValid).WithMessage("Enter a valid email address.");

        RuleFor(x => x.Code).OtpCode();
    }
}

public sealed class ResendOtpCommandValidator : AbstractValidator<ResendOtpCommand>
{
    public ResendOtpCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .Must(EmailRules.IsValid).WithMessage("Enter a valid email address.");
    }
}

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .Must(EmailRules.IsValid).WithMessage("Enter a valid email address.");
    }
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .Must(EmailRules.IsValid).WithMessage("Enter a valid email address.");

        RuleFor(x => x.Code).OtpCode();
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

/// <summary>The single password policy for registration and reset. Mirrors the client-side checks.</summary>
internal static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one letter and one digit.");

    public static IRuleBuilderOptions<T, string> OtpCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Length(OtpPolicy.CodeLength)
            .Matches("^[0-9]+$").WithMessage("The code must contain digits only.");
}

internal static class EmailRules
{
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            return new MailAddress(email.Trim()).Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
