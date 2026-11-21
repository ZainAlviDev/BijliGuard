using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Billing;
using Microsoft.AspNetCore.DataProtection;
using BijliGuard.Infrastructure.Email;
using BijliGuard.Infrastructure.Identity;
using BijliGuard.Infrastructure.Persistence;
using BijliGuard.Infrastructure.Persistence.Repositories;
using BijliGuard.Infrastructure.Security;
using BijliGuard.Infrastructure.Storage;
using BijliGuard.Infrastructure.Persistence.Billing;
using BijliGuard.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BijliGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                // Email verification is enforced by AuthService so the response can explain it.
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            // Needed for Identity's password-reset token. Tokens are created and consumed in one request.
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(o => o.Secret.Length >= 32, "Otp:Secret must be at least 32 characters.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => o.Provider is "Smtp" or "DevelopmentOutbox", "Email:Provider must be Smtp or DevelopmentOutbox.")
            .Validate(o => o.Provider != "DevelopmentOutbox" || environment.IsDevelopment(),
                "Email:Provider DevelopmentOutbox is only allowed in Development.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IEmailOtpStore, EmailOtpStore>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IAuditLog, AuditLogStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBillingRepository, BillingRepository>();
        services.AddScoped<BijliGuard.Application.Consent.IConsentRepository, ConsentRepository>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddDataProtection().SetApplicationName("BijliGuard");
        services.AddSingleton<BijliGuard.Application.Abstractions.IOcrEngine, BijliGuard.Infrastructure.Ocr.TesseractOcrEngine>();
        services.AddSingleton<BijliGuard.Application.Abstractions.IPdfTextReader, BijliGuard.Infrastructure.Pdf.PdfPigTextReader>();
        services.AddSingleton<IFileStorage>(new LocalFileStorage(
            Path.Combine(environment.ContentRootPath, configuration["Storage:RootPath"] ?? "storage")));

        services.AddScoped<IEmailSender>(sp =>
        {
            var provider = sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider;
            return provider switch
            {
                "Smtp" => ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp),
                "DevelopmentOutbox" when environment.IsDevelopment() =>
                    ActivatorUtilities.CreateInstance<DevelopmentOutboxEmailSender>(sp),
                "DevelopmentOutbox" => throw new InvalidOperationException(
                    "Email:Provider DevelopmentOutbox is only allowed in Development."),
                _ => throw new InvalidOperationException($"Unknown Email:Provider '{provider}'.")
            };
        });

        return services;
    }
}
