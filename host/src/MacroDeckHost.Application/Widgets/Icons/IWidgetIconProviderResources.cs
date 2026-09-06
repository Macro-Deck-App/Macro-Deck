using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Ui.Resources;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Widgets.Icons;

/// <summary>
/// Registers the bytes behind an icon-provider action's snapshot into <see cref="IUiResourceStore" />, one
/// entry per widget rather than one per <c>Version</c>: <see cref="ResolveAsync" /> is keyed by widget id
/// and registers every fetch under that same host-generated, stable, widget-scoped resource name, so a new
/// track replaces the previous entry instead of leaking one per track the way keying on the provider's own
/// (arbitrary, potentially oversized, ':'/'/'-bearing) reference would (issue #425 - <c>IUiResourceStore</c>
/// is an unbounded dictionary with no eviction).
///
/// <para>
/// A version already cached is never re-fetched - what makes per-track polling affordable. Oversized or
/// disallowed-media-type bytes are registered as nothing (treated as "no icon") rather than thrown out of
/// a session's resolve loop, and a content fetch that raced a newer version (returned null) never
/// overwrites what is already cached, so the next resolve for the now-current version tries again instead
/// of being stuck on a version that was never actually recorded.
/// </para>
/// </summary>
public interface IWidgetIconProviderResources
{
	Task<UiResource?> ResolveAsync(
		Guid widgetId,
		string version,
		Func<CancellationToken, Task<ActionIconContent?>> fetchContent,
		CancellationToken cancellationToken);
}

public sealed class WidgetIconProviderResources : IWidgetIconProviderResources
{
	/// <summary>The owner id every provider-supplied icon resource is registered under.</summary>
	public const string OwnerId = "app.macro-deck.widget-icon-provider";

	// Defence in depth alongside /api/ui/resources' own X-Content-Type-Options: nosniff and
	// default-src 'none'; sandbox headers - a provider is untrusted plugin code, and an SVG or an HTML
	// document is not a "bytes host can safely serve as an image" the way these four are.
	private static readonly HashSet<string> _allowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/png", "image/jpeg", "image/webp", "image/gif"
	};

	private readonly IUiResourceStore _resourceStore;
	private readonly ILogger _logger;
	private readonly Dictionary<Guid, (string Version, UiResource? Resource)> _cache = new();
	private readonly Lock _sync = new();

	public WidgetIconProviderResources(IUiResourceStore resourceStore, ILogger logger)
	{
		_resourceStore = resourceStore;
		_logger = logger.ForContext<WidgetIconProviderResources>();
	}

	public async Task<UiResource?> ResolveAsync(
		Guid widgetId,
		string version,
		Func<CancellationToken, Task<ActionIconContent?>> fetchContent,
		CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			if (_cache.TryGetValue(widgetId, out var cached) &&
				string.Equals(cached.Version, version, StringComparison.Ordinal))
			{
				return cached.Resource;
			}
		}

		ActionIconContent? content;

		try
		{
			content = await fetchContent(cancellationToken).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // A misbehaving provider must leave a usable widget behind, never fault the resolve loop.
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			content = null;
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Failed to fetch provider icon content for widget {WidgetId}", widgetId);
			content = null;
		}
#pragma warning restore CA1031

		if (content is null)
		{
			// The version already moved on, or the fetch genuinely failed - keep whatever this widget
			// already has rather than blank it, and deliberately do not record this version as resolved:
			// the next resolve (by then reading whatever the provider currently reports) fetches again
			// instead of being stuck believing this version was already tried.
			lock (_sync)
			{
				return _cache.TryGetValue(widgetId, out var previous) ? previous.Resource : null;
			}
		}

		var resource = TryRegister(widgetId, content);

		lock (_sync)
		{
			_cache[widgetId] = (version, resource);
		}

		return resource;
	}

	private UiResource? TryRegister(Guid widgetId, ActionIconContent content)
	{
		if (!_allowedMediaTypes.Contains(content.MediaType))
		{
			_logger.Warning(
				"Provider icon for widget {WidgetId} was not registered: media type '{MediaType}' is not allowed",
				widgetId,
				content.MediaType);
			return null;
		}

		if (content.Data.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			_logger.Warning(
				"Provider icon for widget {WidgetId} was not registered: {ByteLength} bytes exceeds the {Limit} byte limit",
				widgetId,
				content.Data.Length,
				ProtocolLimits.MaxUiResourceBytes);
			return null;
		}

		return _resourceStore.Register(new UiResourceRegistration
		{
			OwnerId = OwnerId,
			Name = widgetId.ToString("N"),
			MediaType = content.MediaType,
			Content = content.Data,
		});
	}
}
