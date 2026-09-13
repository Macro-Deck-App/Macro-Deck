using MacroDeckHost.Application.Auth;
using MacroDeckHost.Auth;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/companion")]
public sealed class CompanionScreenshotsController : ControllerBase
{
	public const long MaxUploadBytes = 16 * 1024 * 1024;

	private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private readonly CompanionCommandRequests _requests;

	public CompanionScreenshotsController(CompanionCommandRequests requests)
	{
		_requests = requests;
	}

	[HttpPost("screenshots/{requestId}")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	[RequestSizeLimit(MaxUploadBytes)]
	public async Task<IActionResult> Upload(string requestId, CancellationToken cancellationToken)
	{
		if (DeviceId() is not { } deviceId ||
			!_requests.IsAwaitingAnswer(deviceId, requestId, CompanionRequestKind.Screenshot))
		{
			return NotFound();
		}

		var png = await ReadBoundedAsync(cancellationToken);
		if (png is null)
		{
			return StatusCode(StatusCodes.Status413PayloadTooLarge);
		}

		if (!png.AsSpan().StartsWith(_pngSignature))
		{
			_requests.TryComplete(deviceId,
				requestId,
				CompanionCommandResult.Failed(CompanionCommandFailure.Failed),
				CompanionRequestKind.Screenshot,
				afterRunning: true);
			return BadRequest();
		}

		return _requests.TryComplete(deviceId,
			requestId,
			CompanionCommandResult.Of(png),
			CompanionRequestKind.Screenshot,
			afterRunning: true)
			? NoContent()
			: NotFound();
	}

	[HttpPost("commands/{requestId}")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult Done(string requestId)
		=> DeviceId() is { } deviceId &&
			_requests.TryComplete(deviceId,
				requestId,
				CompanionCommandResult.Done,
				CompanionRequestKind.Command,
				afterRunning: true)
				? NoContent()
				: NotFound();

	[HttpPost("commands/{requestId}/claim")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult Claim(string requestId) => ClaimAs(requestId, CompanionRequestKind.Command);

	[HttpPost("screenshots/{requestId}/claim")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult ClaimScreenshot(string requestId) => ClaimAs(requestId, CompanionRequestKind.Screenshot);

	private IActionResult ClaimAs(string requestId, CompanionRequestKind kind)
		=> DeviceId() is { } deviceId && _requests.TryClaim(deviceId, requestId, kind) ? NoContent() : NotFound();

	[HttpPost("screenshots/{requestId}/failed")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult ScreenshotFailed(string requestId, [FromBody] CompanionScreenshotFailedRequest request)
		=> Fail(requestId, request, CompanionRequestKind.Screenshot);

	[HttpPost("commands/{requestId}/failed")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult CommandFailed(string requestId, [FromBody] CompanionScreenshotFailedRequest request)
		=> Fail(requestId, request, CompanionRequestKind.Command);

	private IActionResult Fail(string requestId, CompanionScreenshotFailedRequest request, CompanionRequestKind kind)
	{
		var failure = request.Reason switch
		{
			"consentDenied" => CompanionCommandFailure.ConsentDenied,
			"unavailable" => CompanionCommandFailure.Unavailable,
			_ => CompanionCommandFailure.Failed
		};
		return DeviceId() is { } deviceId &&
			_requests.TryComplete(deviceId,
				requestId,
				CompanionCommandResult.Failed(failure),
				kind,
				afterRunning: false)
				? NoContent()
				: NotFound();
	}

	private Guid? DeviceId()
		=> Guid.TryParse(User.FindFirst(AuthDefaults.DeviceClaim)?.Value, out var deviceId) ? deviceId : null;

	private async Task<byte[]?> ReadBoundedAsync(CancellationToken cancellationToken)
	{
		if (Request.ContentLength > MaxUploadBytes)
		{
			return null;
		}

		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		int read;
		while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
		{
			if (buffer.Length + read > MaxUploadBytes)
			{
				return null;
			}

			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}
}

public sealed class CompanionScreenshotFailedRequest
{
	public string? Reason { get; set; }
}
