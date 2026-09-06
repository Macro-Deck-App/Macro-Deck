using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// The live half of the Play Track / Play Playlist picker: what the user typed, what the library
/// answered, and how far down the list they have come.
///
/// <para>
/// The picking itself is not here. A row carries its own answer and the client settles the modal with
/// it, so this session never sees the press - which is also why closing the dialog any other way is a
/// cancellation without this having to know.
/// </para>
/// </summary>
internal sealed class MusicPlayerPickerSession : IUiSession
{
	/// <summary>
	/// How long typing has to pause before the library is asked again. The field already reports at most
	/// ten times a second; this is the second, coarser gate, because each query here is a call into a
	/// remote service rather than a local filter.
	/// </summary>
	private static readonly TimeSpan _searchDebounce = TimeSpan.FromMilliseconds(250);

	private readonly UiView _view;
	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IUiResourceStore _resources;
	private readonly ILogger _logger;
	private readonly string _instanceId;
	private readonly MusicPlayerCatalogItemKind _kind;

	private readonly UiState<MusicPlayerPickerState> _state = new(MusicPlayerPickerState.Loading);
	private readonly UiState<string> _filter = new(string.Empty);
	private readonly UiState<int> _window = new(MusicPlayerPickerView.InitialWindow);

	private readonly UiState<IReadOnlyDictionary<string, UiResource>> _artwork =
		new(new Dictionary<string, UiResource>(StringComparer.Ordinal));

	// The debounced search and the cover pass both run on their own Task.Run threads and both replace one of
	// the two sources below, while a client's dispatch and DisposeAsync do the same from theirs. Taken before
	// the view's own serialization everywhere - a dispatch enters it while holding this - so the two are
	// always acquired in that order.
	private readonly Lock _sync = new();

	private CancellationTokenSource? _search;
	private CancellationTokenSource? _covers;
	private bool _disposed;

	public MusicPlayerPickerSession(
		UiSurface surface,
		IMusicPlayerRegistry registry,
		IMusicPlayerArtworkService artworkService,
		IUiResourceStore resources,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(surface);

		_registry = registry;
		_artworkService = artworkService;
		_resources = resources;
		_instanceId = instanceId;
		_kind = kind;
		_logger = logger.ForContext<MusicPlayerPickerSession>();

		_view = new UiView(surface,
			MusicPlayerPickerView.Build(_state,
				_filter,
				_window,
				_artwork,
				MusicPlayerWidgetIcons.EnsureRegistered(resources),
				[
					UiEventHandler.On(UiComponentEvents.Adjust, OnTyped),
					UiEventHandler.On(UiComponentEvents.Change, OnTyped),
				],
				[UiEventHandler.On(UiComponentEvents.Reveal, OnRevealed)]));

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		Reload(string.Empty, immediate: true);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		lock (_sync)
		{
			return _view.Tree;
		}
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_sync)
		{
			return _view.DrainPatches();
		}
	}

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	public ValueTask DisposeAsync()
	{
		CancellationTokenSource? search;
		CancellationTokenSource? covers;

		// The flag and both sources are taken in one step, so a pass that is between its own cancellation
		// check and its write cannot slip a patch in behind disposal.
		lock (_sync)
		{
			if (_disposed)
			{
				return ValueTask.CompletedTask;
			}

			_disposed = true;
			search = _search;
			covers = _covers;
			_search = null;
			_covers = null;
		}

		Cancel(search);
		Cancel(covers);

		return ValueTask.CompletedTask;
	}

	/// <summary>Cancels and disposes one source. Only ever reached by the thread that took it out of the
	/// field it lived in, so the cancel can never meet another thread's disposal of the same source.</summary>
	private static void Cancel(CancellationTokenSource? source)
	{
		if (source is null)
		{
			return;
		}

		source.Cancel();
		source.Dispose();
	}

	private void OnTyped(UiEventData data)
	{
		var filter = data.TryGetString(out var typed) ? typed : string.Empty;
		if (string.Equals(_filter.Value, filter, StringComparison.Ordinal))
		{
			return;
		}

		// Painted from what was typed rather than from what came back, so the field shows the query
		// while the library is still answering it.
		_filter.Value = filter;
		// A new query is a new list: the old window would otherwise hold a scroll position into results
		// that no longer exist.
		_window.Value = MusicPlayerPickerView.InitialWindow;
		Reload(filter, immediate: false);
	}

	private void OnRevealed(UiEventData data)
	{
		if (!data.TryGetDouble(out var index))
		{
			return;
		}

		var wanted = (int)index + MusicPlayerPickerView.WindowGrowth;
		if (wanted <= _window.Value)
		{
			return;
		}

		_window.Value = wanted;
		StartCovers();
	}

	private void Reload(string filter, bool immediate)
	{
		CancellationTokenSource? previous;
		CancellationToken token;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			previous = _search;
			var current = new CancellationTokenSource();
			_search = current;
			token = current.Token;
		}

		Cancel(previous);

		RunPass(async () =>
			{
				if (!immediate)
				{
					await Task.Delay(_searchDebounce, token).ConfigureAwait(false);
				}

				var state = await LoadAsync(filter, token).ConfigureAwait(false);

				lock (_sync)
				{
					if (_disposed || token.IsCancellationRequested)
					{
						return;
					}

					_state.Value = state;
				}

				StartCovers();
			},
			token);
	}

	/// <summary>
	/// Runs one of the session's fire-and-forget passes. Cancellation is the expected way a pass ends - a
	/// newer query took over, or the dialog closed - and is not reported. Anything else is raised on
	/// <see cref="Faulted" />: a task nobody awaits would otherwise carry its fault to the finalizer, where it
	/// is only ever an unobserved-exception log line that neither the host nor the client can see.
	/// </summary>
	private void RunPass(Func<Task> pass, CancellationToken token)
		=> _ = Task.Run(async () =>
			{
				try
				{
					await pass().ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}
#pragma warning disable CA1031 // The fault of a pass has one place to go, and it is this event.
				catch (Exception exception)
#pragma warning restore CA1031
				{
					Faulted?.Invoke(this, new UiSessionFaultedEventArgs(exception.Message, exception));
				}
			},
			token);

	private async Task<MusicPlayerPickerState> LoadAsync(string filter, CancellationToken cancellationToken)
	{
		if (_registry.GetPlayer(_instanceId) is not IMusicPlayerCatalogProvider catalog)
		{
			return MusicPlayerPickerState.Unsupported;
		}

		try
		{
			var items = await catalog
				.GetCatalogAsync(_instanceId,
					_kind,
					string.IsNullOrWhiteSpace(filter) ? null : filter,
					cancellationToken)
				.ConfigureAwait(false);

			return new MusicPlayerPickerState { Items = items, Available = true, Supported = true };
		}
		catch (OperationCanceledException)
		{
			throw;
		}
#pragma warning disable CA1031 // A library is plugin-owned; its fault leaves a readable dialog, not a dead session.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception,
				"Failed to read the {Kind} catalog for music player instance {InstanceId}",
				_kind,
				_instanceId);

			// An empty list means "this library is empty" only when the read succeeded. Collapsing the
			// two is what made an unreachable player render as "no items found".
			return MusicPlayerPickerState.Unavailable;
		}
	}

	/// <summary>
	/// Fills in the covers for the rows that are actually in the tree.
	///
	/// Bounded by the window on purpose: a cover is one fetch into the provider's service each, and a
	/// library of several hundred would spend them all on rows nobody has scrolled to.
	/// </summary>
	private void StartCovers()
	{
		CancellationTokenSource? previous;
		CancellationToken token;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			previous = _covers;
			var current = new CancellationTokenSource();
			_covers = current;
			token = current.Token;
		}

		Cancel(previous);

		var items = _state.Value.Items;
		var window = Math.Min(_window.Value, items.Count);

		RunPass(async () =>
			{
				var resolved = new Dictionary<string, UiResource>(_artwork.Value, StringComparer.Ordinal);
				var added = false;

				for (var index = 0; index < window && !token.IsCancellationRequested; index++)
				{
					var artworkId = items[index].ArtworkId;
					if (artworkId is not { Length: > 0 } || resolved.ContainsKey(artworkId))
					{
						continue;
					}

					var resource = await RegisterCoverAsync(artworkId, token).ConfigureAwait(false);
					if (resource is null)
					{
						continue;
					}

					resolved[artworkId] = resource;
					added = true;
				}

				// One update rather than one per cover: every cover that arrives would otherwise be its own
				// patch to every client attached to the dialog.
				if (!added)
				{
					return;
				}

				lock (_sync)
				{
					if (_disposed || token.IsCancellationRequested)
					{
						return;
					}

					_artwork.Value = resolved;
				}
			},
			token);
	}

	private async Task<UiResource?> RegisterCoverAsync(string artworkId, CancellationToken cancellationToken)
	{
		try
		{
			var image = await _artworkService
				.GetImage(_instanceId, artworkId, CoverSize, cancellationToken)
				.ConfigureAwait(false);

			if (image is null)
			{
				return null;
			}

			return _resources.Register(new UiResourceRegistration
			{
				OwnerId = MusicPlayerWidgetIcons.OwnerId,
				Name = $"pick.{Slug(artworkId)}",
				MediaType = image.ContentType,
				Content = image.Content,
			});
		}
		catch (OperationCanceledException)
		{
			return null;
		}
#pragma warning disable CA1031 // A provider's artwork fetch is plugin-owned; its fault leaves one row bare.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Debug(exception, "Failed to read artwork {ArtworkId} while picking an item", artworkId);

			return null;
		}
	}

	/// <summary>A row's cover is small on every client; asking for more would cost the library bandwidth
	/// nothing renders.</summary>
	private const int CoverSize = 96;

	private static string Slug(string value)
	{
		var characters = value.Select(character => char.IsLetterOrDigit(character) ? character : '-');

		return string.Concat(characters);
	}
}
