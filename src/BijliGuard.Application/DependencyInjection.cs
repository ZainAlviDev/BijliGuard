using BijliGuard.Application.Auth;
using BijliGuard.Application.Billing;
using BijliGuard.Application.Estimate;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;

namespace BijliGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<BijliGuard.Application.Consent.IConsentService, BijliGuard.Application.Consent.ConsentService>();
        services.AddScoped<BijliGuard.Application.Activity.IActivityService, BijliGuard.Application.Activity.ActivityService>();
        services.AddScoped<IValidator<SaveHouseholdCommand>, SaveHouseholdCommandValidator>();
        services.AddScoped<IValidator<ConfirmBaselineCommand>, ConfirmBaselineCommandValidator>();
        services.AddScoped<IValidator<SubmitReadingCommand>, SubmitReadingCommandValidator>();
        services.AddScoped<IQuickEstimateService, QuickEstimateService>();
        services.AddScoped<IValidator<QuickEstimateCommand>, QuickEstimateValidator>();

        services.AddScoped<IValidator<RegisterCommand>, RegisterCommandValidator>();
        services.AddScoped<IValidator<VerifyEmailCommand>, VerifyEmailCommandValidator>();
        services.AddScoped<IValidator<ResendOtpCommand>, ResendOtpCommandValidator>();
        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<ForgotPasswordCommand>, ForgotPasswordCommandValidator>();
        services.AddScoped<IValidator<ResetPasswordCommand>, ResetPasswordCommandValidator>();

        return services;
    }
}
