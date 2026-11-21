using BijliGuard.Application.Common;

namespace BijliGuard.API.Common;

/// <summary>Standard envelope for every response. See technical design section 7.1.</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error, string? CorrelationId);

public sealed record ApiError(string Code, string Message, IReadOnlyList<FieldError>? Details);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? correlationId) =>
        new(true, data, null, correlationId);

    public static ApiResponse<object?> Empty(string? correlationId) =>
        new(true, null, null, correlationId);

    public static ApiResponse<object?> Failure(string code, string message, IReadOnlyList<FieldError>? details, string? correlationId) =>
        new(false, null, new ApiError(code, message, details), correlationId);
}
