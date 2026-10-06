using System.Globalization;
using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/ui/resources")]
public class UiResourcesController : ControllerBase
{
	private readonly IUiResourceStore _resources;
	private readonly IPluginIconUiResources? _pluginIcons;
	private readonly IIconUiResourceRenditions? _sizedIcons;

	public UiResourcesController(IUiResourceStore resources,
		IPluginIconUiResources? pluginIcons = null,
		IIconUiResourceRenditions? sizedIcons = null)
	{
		_resources = resources;
		_pluginIcons = pluginIcons;
		_sizedIcons = sizedIcons;
	}

	[HttpGet("{resourceId}")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<IActionResult> Get(string resourceId,
		[FromQuery(Name = "v")] string? version = null,
		[FromQuery(Name = "size")] string? size = null,
		CancellationToken cancellationToken = default)
	{
		if (await Find(resourceId, cancellationToken) is not { } resource)
		{
			Response.Headers.CacheControl = "no-store";

			return NotFound();
		}

		Response.Headers.Append("X-Content-Type-Options", "nosniff");
		Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

		if (await FindSized(resourceId, size, cancellationToken) is { } sized)
		{
			return ServeSized(sized, resource, version);
		}

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

	private IActionResult ServeSized(SizedIconResource sized, UiResourceContent resource, string? version)
	{
		var content = sized.Content;
		var etag = $"\"{content.ContentHash}\"";
		Response.Headers.ETag = etag;

		// The v a tree carries names the default rendition, so it can vouch for a sized one only while that
		// rendition is current and was not a degraded stand-in for a variant that failed to derive.
		if (!sized.Stable || (version is not null && !string.Equals(version, resource.ContentHash, StringComparison.Ordinal)))
		{
			Response.Headers.CacheControl = "no-store";

			return File(content.Content.ToArray(), content.MediaType);
		}

		Response.Headers.CacheControl = version is null ? "private, no-cache" : "private, max-age=31536000, immutable";

		if (Request.Headers.IfNoneMatch.Any(value =>
			value is not null && value.Contains(etag, StringComparison.Ordinal)))
		{
			return StatusCode(StatusCodes.Status304NotModified);
		}

		return File(content.Content.ToArray(), content.MediaType);
	}

	private async Task<SizedIconResource?> FindSized(string resourceId, string? size, CancellationToken cancellationToken)
	{
		if (_sizedIcons is null)
		{
			return null;
		}

		var context = IconAppearanceContextQuery.FromRequest(HttpContext);
		if (int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) && requested > 0)
		{
			return await _sizedIcons.TryGetAsync(resourceId, requested, cancellationToken, context);
		}

		return context.IsEmpty
			? null
			: await _sizedIcons.TryGetAsync(resourceId, WidgetIconRenditions.DefaultSize, cancellationToken, context);
	}

	private async Task<UiResourceContent?> Find(string resourceId, CancellationToken cancellationToken)
	{
		if (_resources.TryGet(resourceId, out var resource))
		{
			return resource;
		}

		return _pluginIcons is null ? null : await _pluginIcons.TryGetAsync(resourceId, cancellationToken);
	}
}
