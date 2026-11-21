using System.Text.RegularExpressions;
using BijliGuard.API.Common;
using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Common;
using Serilog.Context;

namespace BijliGuard.API.Middleware;

/// <summary>Reads or creates X-Correlation-Id, returns it, and attaches it to every log entry in the request.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    private static readonly Regex ValidId = new("^[A-Za-z0-9-]{8,64}$", RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = incoming is not null && ValidId.IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>Turns unexpected exceptions into the standard envelope without leaking details.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var correlationId = context.Items[CorrelationIdMiddleware.ItemKey] as string;
            logger.LogError(ex, "Http.UnhandledException {Method} {RouteTemplate}",
                context.Request.Method, context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value);

            if (context.Response.HasStarted) throw;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Failure("INTERNAL_ERROR", "Something went wrong. Please try again.", null, correlationId));
        }
    }
}

public sealed class HttpCorrelationIdAccessor(IHttpContextAccessor accessor) : ICorrelationIdAccessor
{
    public string? CorrelationId =>
        accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
}
