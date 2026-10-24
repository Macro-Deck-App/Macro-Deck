using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/ui/sessions")]
public sealed class UiUploadsController : ControllerBase
{
	public const long MaxUploadBytes = ProtocolLimits.MaxUiResourceBytes;

	private readonly UiUploadStore _uploads;

	public UiUploadsController(UiUploadStore uploads)
	{
		_uploads = uploads;
	}

	[HttpPost("{sessionId}/uploads")]
	[Authorize(Policy = AuthPolicies.Admin)]
	[RequestSizeLimit(MaxUploadBytes)]
	public async Task<IActionResult> Upload(
		string sessionId,
		[FromQuery] string? nodeId,
		CancellationToken cancellationToken)
	{
		var content = await ReadBoundedAsync(cancellationToken);
		if (content is null)
		{
			return StatusCode(StatusCodes.Status413PayloadTooLarge);
		}

		// The owner principal is derived exactly as the UI socket derives it when the session is opened.
		var principal = User.FindFirst(AuthDefaults.DeviceClaim)?.Value ?? string.Empty;
		var result = _uploads.Add(sessionId, nodeId, principal, content);

		return result.Outcome switch
		{
			UiUploadOutcome.Stored => Ok(new UiUploadResponse
			{
				UploadId = result.Upload!.UploadId,
				Length = result.Upload.Content.Length,
				ContentHash = result.Upload.ContentHash
			}),
			UiUploadOutcome.Full => StatusCode(StatusCodes.Status429TooManyRequests),
			_ => NotFound()
		};
	}

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

public sealed class UiUploadResponse
{
	public required string UploadId { get; init; }

	public required long Length { get; init; }

	public required string ContentHash { get; init; }
}
