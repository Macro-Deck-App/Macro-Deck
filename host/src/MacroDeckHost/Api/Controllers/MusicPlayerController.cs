using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/music-player")]
public class MusicPlayerController : ControllerBase
{
	private readonly IMusicPlayerArtworkService _artworkService;

	public MusicPlayerController(IMusicPlayerArtworkService artworkService)
	{
		_artworkService = artworkService;
	}

	[HttpGet("artwork/{artworkId}")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<IActionResult> GetArtwork(string artworkId,
		[FromQuery] string instanceId,
		[FromQuery] int? size,
		CancellationToken ct)
	{
		var etag = _artworkService.GetETag(artworkId, size);
		if (Request.Headers.IfNoneMatch.Any(v => v is not null && v.Contains(etag)))
		{
			Response.Headers.ETag = etag;
			return StatusCode(StatusCodes.Status304NotModified);
		}

		var image = await _artworkService.GetImage(instanceId, artworkId, size, ct);
		if (image is null)
		{
			Response.Headers.CacheControl = "no-store";
			return NotFound();
		}

		Response.Headers.CacheControl = "public, max-age=31536000, immutable";
		Response.Headers.ETag = image.ETag;
		return File(image.Content, image.ContentType);
	}
}
