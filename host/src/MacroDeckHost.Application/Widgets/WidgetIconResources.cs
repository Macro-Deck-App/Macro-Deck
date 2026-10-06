using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Widgets;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetIconResources
{
	/// <summary>Resolves <paramref name="reference" /> to a drawable resource, registering it with the UI
	/// resource store the first time it is asked for. <c>null</c> when <paramref name="reference" /> is
	/// absent, its provider type is not registered, or the icon cannot be read - never a thrown
	/// exception.</summary>
	Task<UiResource?> ResolveAsync(WidgetIconReference? reference, CancellationToken cancellationToken);

	Task<UiResource?> ResolveAsync(WidgetIconReference? reference,
		WidgetIconLimit limit,
		CancellationToken cancellationToken);

	/// <summary>Forgets <paramref name="iconId" />'s cached <see cref="UiResource" /> handle in this
	/// bridge's own LRU, if any, so the next <see cref="ResolveAsync" /> re-reads the icon's bytes and
	/// registers a fresh handle for them. Called when the icon's bytes changed or the icon was deleted.
	/// This does <b>not</b> touch the bytes a handle already registered - those live in
	/// <c>IUiResourceStore</c>'s own store, which this LRU never reaches - so an open session still
	/// serving a handle from before the evict keeps drawing the old bytes until it re-resolves the icon
	/// itself; the eviction only stops a <i>future</i> resolve from handing out the stale handle.</summary>
	void Evict(Guid iconId);
}

public enum WidgetIconLimit
{
	Host,
	Protocol
}

/// <summary>
/// Bridges <see cref="IWidgetIconSourceRegistry" /> into <see cref="IUiResourceStore" /> so a widget tree
/// can draw a resolved icon by resource handle instead of carrying its bytes. Deliberately generic rather
/// than slider-specific: the Action Button migration reuses this exactly as it stands.
///
/// <para>
/// Bounded by a simple LRU over <see cref="MaxEntries" />: the Action Button migration is the follow-up
/// this type's own doc comment named, since nearly every Action Button carries an icon. <see cref="Evict" />
/// additionally drops a specific entry the moment its bytes change or it is deleted, which bounding alone
/// would not catch for an icon still within the LRU window.
/// </para>
/// </summary>
public sealed class WidgetIconResources : IWidgetIconResources
{
	/// <summary>The owner id every icon resource this bridge registers is namespaced under.</summary>
	public const string OwnerId = "app.macro-deck.widget-icon";

	// Comfortably above how many distinct icons a real deck's worth of Action Buttons and Sliders puts on
	// screen at once, while still bounding the process's lifetime memory against an install that has cycled
	// through hundreds of icons over time.
	internal const int MaxEntries = 512;

	private readonly IWidgetIconSourceRegistry _sources;
	private readonly IUiResourceStore _resourceStore;
	private readonly IIconPackCache _iconPackCache;
	private readonly ILogger _logger;

	// Keyed by type+reference and holding only a resource that resolved: a transient read failure must not
	// poison every later poll for a reference that is perfectly fine, the way caching the failed task would.
	// _order tracks recency (most-recently-used at the end) for the bound above; both are guarded by _sync
	// since an LRU touch on a read has to move a node without racing an insert or an Evict.
	private readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> _order = new();
	private readonly LinkedList<CacheKey> _recency = new();
	private readonly Dictionary<CacheKey, UiResource> _registered = new();
	private readonly Lock _sync = new();

	public WidgetIconResources(IWidgetIconSourceRegistry sources,
		IUiResourceStore resourceStore,
		ILogger logger,
		IIconPackCache iconPackCache)
	{
		_sources = sources;
		_resourceStore = resourceStore;
		_iconPackCache = iconPackCache;
		_logger = logger.ForContext<WidgetIconResources>();
	}

	public void Evict(Guid iconId)
	{
		// The icon-pack notification handler only ever knows a Guid, never the reference string it was
		// stored as - constructing the reference this way (never Guid.TryParse) keeps this bridge itself
		// out of the business of parsing provider-specific reference formats.
		var reference = WidgetIconReference.IconPack(iconId.ToString());

		lock (_sync)
		{
			foreach (var key in _registered.Keys
				.Where(key => key.Reference.Type == reference.Type &&
					string.Equals(key.Reference.Reference, reference.Reference, StringComparison.Ordinal))
				.ToList())
			{
				if (_order.Remove(key, out var node))
				{
					_recency.Remove(node);
				}

				_registered.Remove(key);
			}
		}
	}

	public Task<UiResource?> ResolveAsync(WidgetIconReference? reference, CancellationToken cancellationToken)
		=> ResolveAsync(reference, WidgetIconLimit.Host, cancellationToken);

	public async Task<UiResource?> ResolveAsync(WidgetIconReference? reference,
		WidgetIconLimit limit,
		CancellationToken cancellationToken)
	{
		if (reference is not { } value || string.IsNullOrEmpty(value.Reference))
		{
			return null;
		}

		var key = new CacheKey(value, limit);
		if (TryGetCached(key, out var cached))
		{
			return cached;
		}

		if (_sources.Find(value.Type) is not { } source)
		{
			return null;
		}

		var target = Target(value, limit);

		try
		{
			var maxBytes = MaxBytes(limit);
			var rendition = await WidgetIconRenditions.ProduceAsync(source,
					target.Reference,
					WidgetIconRenditions.DefaultSize,
					maxBytes,
					cancellationToken)
				.ConfigureAwait(false);

			switch (rendition.Status)
			{
				case WidgetIconRenditionStatus.Rendered:
				{
					var resource = _resourceStore.Register(new UiResourceRegistration
					{
						OwnerId = OwnerId,
						Name = target.Name,
						MediaType = rendition.MediaType!,
						Content = rendition.Content!,
						MaxBytes = maxBytes,
						Variation = target.Variation,
					});

					Store(key, resource);

					return resource;
				}

				case WidgetIconRenditionStatus.TooLarge:
					_logger.Warning(
						"Widget icon '{Type}:{Reference}' was not registered: no rendition fits the {Limit} byte limit",
						value.Type,
						value.Reference,
						maxBytes);

					return null;

				default:
					return null;
			}
		}
#pragma warning disable CA1031 // A missing or unreadable icon must leave a usable widget behind, never fault the session.
		catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
		{
			_logger.Warning(exception,
				"Failed to resolve widget icon '{Type}:{Reference}'",
				value.Type,
				value.Reference);

			return null;
		}
	}

	internal static string ResourceName(WidgetIconReference reference, WidgetIconLimit limit)
		=> limit == WidgetIconLimit.Protocol
			? $"{reference.Type}.{reference.Reference}{ProtocolNameSuffix}"
			: $"{reference.Type}.{reference.Reference}";

	internal const string ProtocolNameSuffix = ".protocol";

	internal const string AppearanceNameSuffix = ".a";

	private ResolveTarget Target(WidgetIconReference reference, WidgetIconLimit limit)
	{
		var plain = new ResolveTarget(reference.Reference, ResourceName(reference, limit), null);
		if (reference.Type != WidgetIconReference.IconPackType ||
			!Guid.TryParse(reference.Reference, out var iconId) ||
			_iconPackCache.GetIconById(iconId) is not { AppearanceOfId: null } icon)
		{
			return plain;
		}

		var appearances = _iconPackCache.GetAppearances(icon.Id);
		if (appearances.Count == 0 || reference.Appearance == WidgetIconReference.DefaultAppearance)
		{
			return plain;
		}

		if (reference.Appearance is { } pin && IconAppearanceSelector.FindPinned(appearances, pin) is { } asset)
		{
			var pinned = WidgetIconReference.IconPack(asset.Id.ToString());
			return new ResolveTarget(pinned.Reference, ResourceName(pinned, limit), null);
		}

		return limit == WidgetIconLimit.Protocol
			? plain
			: new ResolveTarget(reference.Reference,
				ResourceName(reference, limit) + AppearanceNameSuffix,
				IconImageVersion.Of(icon, appearances));
	}

	private readonly record struct ResolveTarget(string Reference, string Name, string? Variation);

	private static int MaxBytes(WidgetIconLimit limit)
		=> limit == WidgetIconLimit.Protocol
			? ProtocolLimits.MaxUiResourceBytes
			: HostUiResourceLimits.MaxHostIconResourceBytes;

	private bool TryGetCached(CacheKey reference, out UiResource resource)
	{
		lock (_sync)
		{
			if (!_registered.TryGetValue(reference, out resource!))
			{
				return false;
			}

			// A read is a touch: move to the most-recently-used end so eviction takes the entry nobody has
			// asked for in the longest time, not simply the one that happened to be registered first.
			if (_order.TryGetValue(reference, out var node))
			{
				_recency.Remove(node);
				_recency.AddLast(node);
			}

			return true;
		}
	}

	private void Store(CacheKey reference, UiResource resource)
	{
		lock (_sync)
		{
			if (_order.TryGetValue(reference, out var existing))
			{
				_recency.Remove(existing);
			}

			_order[reference] = _recency.AddLast(reference);
			_registered[reference] = resource;

			while (_recency.Count > MaxEntries)
			{
				var oldest = _recency.First!;
				_recency.RemoveFirst();
				_order.Remove(oldest.Value);
				_registered.Remove(oldest.Value);
			}
		}
	}

	private readonly record struct CacheKey(WidgetIconReference Reference, WidgetIconLimit Limit);
}
