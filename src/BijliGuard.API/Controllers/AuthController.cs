using System.Security.Claims;
using BijliGuard.API.Common;
using BijliGuard.API.Middleware;
using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Auth;
using BijliGuard.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace BijliGuard.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;

    [HttpPost("register")]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(command, ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status202Accepted, ApiResponse.Ok(result.Value, CorrelationId))
            : Fail(result.Error!);
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth-verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailCommand command, CancellationToken ct)
    {
        var result = await auth.VerifyEmailAsync(command, ct);
        return result.IsSuccess ? Ok(ApiResponse.Empty(CorrelationId)) : Fail(result.Error!);
    }

    [HttpPost("resend-otp")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpCommand command, CancellationToken ct)
    {
        var result = await auth.ResendOtpAsync(command, ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status202Accepted, ApiResponse.Empty(CorrelationId))
            : Fail(result.Error!);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct)
    {
        var result = await auth.LoginAsync(command, ct);
        if (!result.IsSuccess) return Fail(result.Error!);

        SetRefreshCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAt);
        return Ok(ApiResponse.Ok(result.Value, CorrelationId));
    }

    /// <summary>
    /// Renews the session. Browsers send the HttpOnly refresh cookie; mobile apps send the token in the body.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth-verify")]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken ?? Request.Cookies[RefreshCookieName];
        var result = await auth.RefreshAsync(token ?? string.Empty, ct);
        if (!result.IsSuccess)
        {
            DeleteRefreshCookie();
            return Fail(result.Error!);
        }

        SetRefreshCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAt);
        return Ok(ApiResponse.Ok(result.Value, CorrelationId));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? request, CancellationToken ct)
    {
        await auth.LogoutAsync(request?.RefreshToken ?? Request.Cookies[RefreshCookieName] ?? string.Empty, ct);
        DeleteRefreshCookie();
        return NoContent();
    }

    private const string RefreshCookieName = "bijliguard_refresh";

    // HttpOnly: scripts cannot read it. Path limits it to the auth endpoints. Strict SameSite blocks cross-site sends.
    // Secure is required outside Development, where the site runs over HTTPS.
    private void SetRefreshCookie(string token, DateTimeOffset expiresAt) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = expiresAt
        });

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Path = "/api/v1/auth",
            SameSite = SameSiteMode.Strict
        });

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct)
    {
        var result = await auth.ForgotPasswordAsync(command, ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status202Accepted, ApiResponse.Empty(CorrelationId))
            : Fail(result.Error!);
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-verify")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken ct)
    {
        var result = await auth.ResetPasswordAsync(command, ct);
        return result.IsSuccess ? Ok(ApiResponse.Empty(CorrelationId)) : Fail(result.Error!);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var id = User.FindFirstValue("sub");
        var email = User.FindFirstValue("email");
        var name = User.FindFirstValue("name");
        return Ok(ApiResponse.Ok(new { id, email, fullName = name }, CorrelationId));
    }

    private IActionResult Fail(Error error) =>
        StatusCode(StatusFor(error.Code),
            ApiResponse.Failure(error.Code, error.Message, error.Fields, CorrelationId));

    private static int StatusFor(string code) => code switch
    {
        ErrorCodes.ValidationFailed => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.EmailAlreadyRegistered => StatusCodes.Status409Conflict,
        ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.TokenInvalid => StatusCodes.Status401Unauthorized,
        ErrorCodes.TokenReused => StatusCodes.Status401Unauthorized,
        ErrorCodes.EmailNotVerified => StatusCodes.Status403Forbidden,
        ErrorCodes.AccountLocked => StatusCodes.Status423Locked,
        ErrorCodes.OtpTooManyAttempts => StatusCodes.Status429TooManyRequests,
        ErrorCodes.OtpResendCooldown => StatusCodes.Status429TooManyRequests,
        ErrorCodes.EmailDeliveryFailed => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };
}

public sealed record RefreshRequest(string RefreshToken);
