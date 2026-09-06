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
	public IActionResult Get(string resourceId)
	{
		if (!_resources.TryGet(resourceId, out var resource))
		{
			Response.Headers.CacheControl = "no-store";

			return NotFound();
		}

		var etag = $"\"{resource.ContentHash}\"";

		if (Request.Headers.IfNoneMatch.Any(value =>
			value is not null && value.Contains(etag, StringComparison.Ordinal)))
		{
			Response.Headers.ETag = etag;

			return StatusCode(StatusCodes.Status304NotModified);
		}

		Response.Headers.Append("X-Content-Type-Options", "nosniff");
		Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

		Response.Headers.CacheControl = "private, max-age=31536000, immutable";
		Response.Headers.ETag = etag;

		return File(resource.Content.ToArray(), resource.MediaType);
	}
}
