using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/ui/resources")]
public class UiResourcesController : ControllerBase
{
	private readonly IUiResourceStore _resources;

	public UiResourcesController(IUiResourceStore resources) => _resources = resources;

	[HttpGet("{resourceId}")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult Get(string resourceId, [FromQuery(Name = "v")] string? version = null)
	{
		if (!_resources.TryGet(resourceId, out var resource))
		{
			Response.Headers.CacheControl = "no-store";

			return NotFound();
		}

		Response.Headers.Append("X-Content-Type-Options", "nosniff");
		Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

		// A resource registered again under its name keeps its id, so a URL naming an older version must not
		// cache the current bytes as that version.
		if (version is not null && !string.Equals(version, resource.ContentHash, StringComparison.Ordinal))
		{
			Response.Headers.CacheControl = "no-store";

			return File(resource.Content.ToArray(), resource.MediaType);
		}

		var etag = $"\"{resource.ContentHash}\"";

		if (Request.Headers.IfNoneMatch.Any(value =>
			value is not null && value.Contains(etag, StringComparison.Ordinal)))
		{
			Response.Headers.ETag = etag;

			return StatusCode(StatusCodes.Status304NotModified);
		}

		Response.Headers.CacheControl = "private, max-age=31536000, immutable";
		Response.Headers.ETag = etag;

		return File(resource.Content.ToArray(), resource.MediaType);
	}
}
