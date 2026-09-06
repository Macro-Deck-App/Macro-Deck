using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Auth;
using MacroDeck.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public class IntegrationIconController : ControllerBase
{
	private readonly IIntegrationRegistry _registry;

	public IntegrationIconController(IIntegrationRegistry registry)
	{
		_registry = registry;
	}

	[HttpGet("{id}/icon")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult GetIcon(string id)
	{
		var integration = _registry.Integrations.FirstOrDefault(i => i.Id == id);
		if (integration is not IIntegrationIconProvider iconProvider)
		{
			return NotFound();
		}

		var bytes = iconProvider.GetIcon();
		var version = IntegrationIconVersion.Of(bytes);
		if (version is null)
		{
			return NotFound();
		}

		// The URL carries the integration id, which stays the same when a plugin ships a new icon -
		// so the response must be revalidated rather than reused on age alone (issue #754). The ETag
		// keeps an unchanged icon a 304 instead of a re-download.
		var etag = $"\"{version}\"";
		if (Request.Headers.IfNoneMatch.Any(v => v is not null && v.Contains(etag)))
		{
			Response.Headers.ETag = etag;
			return StatusCode(StatusCodes.Status304NotModified);
		}

		Response.Headers.CacheControl = "no-cache";
		Response.Headers.ETag = etag;
		return File(bytes, iconProvider.IconMimeType);
	}
}
