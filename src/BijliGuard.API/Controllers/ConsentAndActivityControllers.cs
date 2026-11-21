using BijliGuard.API.Common;
using BijliGuard.Application.Activity;
using BijliGuard.Application.Consent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BijliGuard.API.Controllers;

public sealed record AcceptConsentRequest(string Version);

[ApiController]
[Authorize]
[Route("api/v1/consent")]
public sealed class ConsentController(IConsentService consent) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Respond(await consent.GetAsync(UserId, ct));

    [HttpPost]
    public async Task<IActionResult> Accept([FromBody] AcceptConsentRequest request, CancellationToken ct) =>
        Respond(await consent.AcceptAsync(UserId, request.Version ?? string.Empty, ct));
}

[ApiController]
[Authorize]
[Route("api/v1/logs")]
public sealed class LogsController(IActivityService activity) : AppControllerBase
{
    /// <summary>The caller's own recent activity, newest first.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Respond(await activity.ListMineAsync(UserId, ct));
}
