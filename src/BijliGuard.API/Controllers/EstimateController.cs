using BijliGuard.API.Common;
using BijliGuard.API.Middleware;
using BijliGuard.Application.Common;
using BijliGuard.Application.Estimate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BijliGuard.API.Controllers;

/// <summary>Public quick check. No account and no storage. Rate limited per client IP.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/estimate")]
public sealed class EstimateController(IQuickEstimateService estimates) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string;

    [HttpPost]
    [EnableRateLimiting("public")]
    public IActionResult Calculate([FromBody] QuickEstimateCommand command)
    {
        var result = estimates.Calculate(command);
        if (result.IsSuccess) return Ok(ApiResponse.Ok(result.Value, CorrelationId));

        return StatusCode(StatusCodes.Status422UnprocessableEntity,
            ApiResponse.Failure(result.Error!.Code, result.Error.Message, result.Error.Fields, CorrelationId));
    }
}
