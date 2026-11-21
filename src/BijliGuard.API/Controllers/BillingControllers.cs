using BijliGuard.API.Common;
using BijliGuard.Application.Billing;
using BijliGuard.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace BijliGuard.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household")]
public sealed class HouseholdController(IBillingService billing) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Respond(await billing.GetHouseholdAsync(UserId, ct));

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveHouseholdCommand command, CancellationToken ct) =>
        Respond(await billing.SaveHouseholdAsync(UserId, command, ct));

    /// <summary>Changes only the cycle target (50 to 2000 units). The company and reference stay as they are.</summary>
    [HttpPut("target")]
    public async Task<IActionResult> ChangeTarget([FromBody] ChangeTargetCommand command, CancellationToken ct) =>
        Respond(await billing.ChangeTargetAsync(UserId, command, ct));
}

[ApiController]
[Authorize]
[Route("api/v1/bills")]
public sealed class BillsController(IBillingService billing) : AppControllerBase
{
    /// <summary>Accepts a JPG, PNG or PDF of at most 10 MB. The type is checked from the file content.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Fail(new Error(ErrorCodes.EmptyFile, "Choose a bill file to upload."));

        await using var stream = file.OpenReadStream();
        return Respond(await billing.UploadBillAsync(UserId, stream, ct), StatusCodes.Status201Created);
    }

    /// <summary>Reads a bill sent for preview (JPG or PNG, 10 MB at most). Returns values only; nothing is stored.</summary>
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Preview([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Fail(new Error(ErrorCodes.EmptyFile, "Choose a bill file to read."));

        await using var stream = file.OpenReadStream();
        return Respond(await billing.PreviewBillAsync(UserId, stream, ct));
    }

    /// <summary>Proposes the billing values from an uploaded bill image. Returns values only; nothing is saved.</summary>
    [HttpPost("{id:guid}/extract")]
    public async Task<IActionResult> Extract(Guid id, CancellationToken ct) =>
        Respond(await billing.ExtractBillAsync(UserId, id, ct));

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> File(Guid id, CancellationToken ct)
    {
        var result = await billing.GetBillFileAsync(UserId, id, ct);
        if (!result.IsSuccess) return Fail(result.Error!);

        // Bills are personal: never cached by browsers or shared proxies.
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }
}

[ApiController]
[Authorize]
[Route("api/v1/baseline")]
public sealed class BaselineController(IBillingService billing) : AppControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Confirm([FromBody] ConfirmBaselineCommand command, CancellationToken ct) =>
        Respond(await billing.ConfirmBaselineAsync(UserId, command, ct), StatusCodes.Status201Created);
}

[ApiController]
[Authorize]
[Route("api/v1/readings")]
public sealed class ReadingsController(IBillingService billing) : AppControllerBase
{
    /// <summary>
    /// Keeps a meter photo as proof (JPG or PNG, 5 MB at most). Metadata is removed. Returns 202: no automatic reading
    /// is suggested in this release, so the user always types the number.
    /// </summary>
    [HttpPost("photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Fail(new Error(ErrorCodes.EmptyFile, "Choose a meter photo to upload."));

        await using var stream = file.OpenReadStream();
        return Respond(await billing.UploadReadingPhotoAsync(UserId, stream, ct), StatusCodes.Status202Accepted);
    }

    /// <summary>Proposes the meter reading from an uploaded photo. The user confirms it before it is saved.</summary>
    [HttpPost("photo/{id:guid}/extract")]
    public async Task<IActionResult> ExtractPhoto(Guid id, CancellationToken ct) =>
        Respond(await billing.ExtractMeterPhotoAsync(UserId, id, ct));

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitReadingCommand command, CancellationToken ct) =>
        Respond(await billing.SubmitReadingAsync(UserId, command, ct), StatusCodes.Status201Created);

    [HttpPost("{id:guid}/confirm-rollover")]
    public async Task<IActionResult> ConfirmRollover(Guid id, CancellationToken ct) =>
        Respond(await billing.ConfirmRolloverAsync(UserId, id, ct));

    [HttpPost("{id:guid}/correct")]
    public async Task<IActionResult> Correct(Guid id, [FromBody] CorrectReadingCommand command, CancellationToken ct) =>
        Respond(await billing.CorrectReadingAsync(UserId, id, command, ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Respond(await billing.ListReadingsAsync(UserId, ct));
}

[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IBillingService billing) : AppControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Respond(await billing.GetDashboardAsync(UserId, ct));
}
