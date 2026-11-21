using System.Security.Claims;
using BijliGuard.API.Middleware;
using BijliGuard.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BijliGuard.API.Common;

/// <summary>Shared behaviour for authenticated controllers: the caller's identity and consistent responses.</summary>
public abstract class AppControllerBase : ControllerBase
{
    protected string? CorrelationId => HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;

    /// <summary>The signed-in user, taken from the verified token. Never taken from the request body.</summary>
    protected Guid UserId
    {
        get
        {
            var sub = User.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : throw new UnauthorizedAccessException("Missing user identity.");
        }
    }

    protected IActionResult Respond<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
            return StatusCode(successStatus, ApiResponse.Ok(result.Value, CorrelationId));
        return Fail(result.Error!);
    }

    protected IActionResult Respond(Result result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
            return StatusCode(successStatus, ApiResponse.Empty(CorrelationId));
        return Fail(result.Error!);
    }

    protected IActionResult Fail(Error error) =>
        StatusCode(StatusFor(error.Code), ApiResponse.Failure(error.Code, error.Message, error.Fields, CorrelationId));

    /// <summary>Conflicts with the current state return 409, missing things 404, and business rule failures 422.</summary>
    protected static int StatusFor(string code) => code switch
    {
        ErrorCodes.HouseholdRequired => StatusCodes.Status409Conflict,
        ErrorCodes.BaselineRequired => StatusCodes.Status409Conflict,
        ErrorCodes.DuplicateReading => StatusCodes.Status409Conflict,
        ErrorCodes.CycleEnded => StatusCodes.Status409Conflict,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.FileTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorCodes.UnsupportedFile => StatusCodes.Status415UnsupportedMediaType,
        ErrorCodes.OcrUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity
    };
}
