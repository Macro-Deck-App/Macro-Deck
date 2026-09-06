using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Resources;
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

	/// <summary>Forgets <paramref name="iconId" />'s cached <see cref="UiResource" /> handle in this
	/// bridge's own LRU, if any, so the next <see cref="ResolveAsync" /> re-reads the icon's bytes and
	/// registers a fresh handle for them. Called when the icon's bytes changed or the icon was deleted.
	/// This does <b>not</b> touch the bytes a handle already registered - those live in
	/// <c>IUiResourceStore</c>'s own store, which this LRU never reaches - so an open session still
	/// serving a handle from before the evict keeps drawing the old bytes until it re-resolves the icon
	/// itself; the eviction only stops a <i>future</i> resolve from handing out the stale handle.</summary>
	void Evict(Guid iconId);
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

	// A deck tile never needs a sharper icon than this, and the retired Slider component used the same
	// rendition size. The further candidates only come into play when the first is too large to register.
	private static readonly (int Size, bool StaticFrame)[] RenditionCandidates =
	[
		(256, false),
		(128, false),
		(128, true)
	];

	// Comfortably above how many distinct icons a real deck's worth of Action Buttons and Sliders puts on
	// screen at once, while still bounding the process's lifetime memory against an install that has cycled
	// through hundreds of icons over time.
	internal const int MaxEntries = 512;

	private readonly IWidgetIconSourceRegistry _sources;
	private readonly IUiResourceStore _resourceStore;
	private readonly ILogger _logger;

	// Keyed by type+reference and holding only a resource that resolved: a transient read failure must not
	// poison every later poll for a reference that is perfectly fine, the way caching the failed task would.
	// _order tracks recency (most-recently-used at the end) for the bound above; both are guarded by _sync
	// since an LRU touch on a read has to move a node without racing an insert or an Evict.
	private readonly Dictionary<WidgetIconReference, LinkedListNode<WidgetIconReference>> _order = new();
	private readonly LinkedList<WidgetIconReference> _recency = new();
	private readonly Dictionary<WidgetIconReference, UiResource> _registered = new();
	private readonly Lock _sync = new();

	public WidgetIconResources(IWidgetIconSourceRegistry sources, IUiResourceStore resourceStore, ILogger logger)
	{
		_sources = sources;
		_resourceStore = resourceStore;
		_logger = logger.ForContext<WidgetIconResources>();
	}

	public void Evict(Guid iconId)
	{
		// The icon-pack notification handler only ever knows a Guid, never the reference string it was
		// stored as - constructing the reference this way (never Guid.TryParse) keeps this bridge itself
		// out of the business of parsing provider-specific reference formats.
		var key = WidgetIconReference.IconPack(iconId.ToString());

		lock (_sync)
		{
			if (_order.Remove(key, out var node))
			{
				_recency.Remove(node);
			}

			_registered.Remove(key);
		}
	}

	public async Task<UiResource?> ResolveAsync(WidgetIconReference? reference, CancellationToken cancellationToken)
	{
		if (reference is not { } value || string.IsNullOrEmpty(value.Reference))
		{
			return null;
		}

		if (TryGetCached(value, out var cached))
		{
			return cached;
		}

		if (_sources.Find(value.Type) is not { } source)
		{
			return null;
		}

		try
		{
			// Deliberately not WebP, even though every current desktop browser reads it: these bytes are
			// registered once, server-side, and served by resource id from a store that does no content
			// negotiation - so whatever is chosen here is what every client gets. Safari only learned
			// WebP in 14, which left every icon blank on iOS 13 and older. The fallback is a disk-cached
			// PNG, or GIF for an animated icon, so animation survives; the cost is a somewhat larger
			// rendition at 256px, which is the cheaper trade on a local network than an unreadable icon.
			//
			// Registering both encodings and negotiating in the controller was the alternative, and was
			// rejected: IUiResourceStore is an unbounded in-memory dictionary with no eviction (issue
			// #425), and doubling every icon in it costs more than the bytes saved on the wire.
			//
			// A GIF of a long animation can outgrow what a UI resource may carry - 180 frames at 256px
			// came out at 3.6 MB against a 2 MB limit, and the store refuses it. Rather than draw no icon
			// at all, the rendition steps down to 128px and finally to the animation's first frame, so
			// the tile always shows the icon that was picked, at worst without its motion.
			foreach (var (size, staticFrame) in RenditionCandidates)
			{
				var image = await source
					.GetImageAsync(value.Reference, size, acceptWebp: false, staticFrame, cancellationToken)
					.ConfigureAwait(false);

				if (image is null)
				{
					return null;
				}

				byte[] content;
				try
				{
					using var memory = new MemoryStream();
					await image.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
					content = memory.ToArray();
				}
				finally
				{
					await image.Content.DisposeAsync().ConfigureAwait(false);
				}

				if (content.Length > ProtocolLimits.MaxUiResourceBytes)
				{
					continue;
				}

				var resource = _resourceStore.Register(new UiResourceRegistration
				{
					OwnerId = OwnerId,
					Name = $"{value.Type}.{value.Reference}",
					MediaType = image.MediaType,
					Content = content,
				});

				Store(value, resource);

				return resource;
			}

			_logger.Warning(
				"Widget icon '{Type}:{Reference}' was not registered: no rendition fits the {Limit} byte limit",
				value.Type,
				value.Reference,
				ProtocolLimits.MaxUiResourceBytes);

			return null;
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

	private bool TryGetCached(WidgetIconReference reference, out UiResource resource)
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

	private void Store(WidgetIconReference reference, UiResource resource)
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
}
