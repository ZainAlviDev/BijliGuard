using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using BijliGuard.API.Common;
using BijliGuard.API.Middleware;
using BijliGuard.Application;
using BijliGuard.Application.Abstractions;
using BijliGuard.Infrastructure;
using BijliGuard.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Serilog;

const string WebCorsPolicy = "web";

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BijliGuard")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    // Keep "sub" and "email" as written in the token instead of remapping them to .NET claim URIs.
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
        {
            bearer.MapInboundClaims = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Value.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Value.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "sub",
                RoleClaimType = ClaimTypes.Role
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Strict: credential and code-sending endpoints. 5 requests per minute per client IP.
        options.AddPolicy("auth-strict", context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        // Public quick check: anonymous, so limited per IP to stop scripted abuse. 20 per minute.
        options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        // Verify: 10 requests per minute per client IP, allows a few retries of a typed code.
        options.AddPolicy("auth-verify", context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
        .ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var details = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors.Select(err =>
                        new BijliGuard.Application.Common.FieldError(e.Key, err.ErrorMessage)))
                    .ToList();
                var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;
                return new ObjectResult(ApiResponse.Failure("VALIDATION_FAILED", "Some fields are invalid.", details, correlationId))
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity
                };
            };
        });

    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks();

    // Only the web app origins listed in configuration may call the API from a browser.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddPolicy(WebCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        // Credentials let the browser send the HttpOnly refresh cookie. Only the listed origins may use them.
        .AllowCredentials()
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id")
        .WithExposedHeaders("X-Correlation-Id")));

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "Http.RequestCompleted {RequestMethod} {RequestPath} {StatusCode} {Elapsed:0.0}ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        app.UseHttpsRedirection();
    }

    app.UseCors(WebCorsPolicy);
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/api/v1/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "BijliGuard host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string ClientKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
