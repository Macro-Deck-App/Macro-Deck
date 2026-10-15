using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed class SteamPlayingSession : IUiSession
{
	private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

	private readonly ISteamSource? _source;
	private readonly SteamPlayingOptions _options;
	private readonly SteamArtworkCache _artwork;
	private readonly UiResource? _glyph;
	private readonly TimeProvider _time;
	private readonly UiState<SteamPlayingState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();
	private bool _disposed;

	public SteamPlayingSession(
		UiSurface surface,
		ISteamSource? source,
		SteamPlayingOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph,
		TimeProvider time,
		Func<UiState<SteamPlayingState>, UiElement> build)
	{
		_source = source;
		_options = options;
		_artwork = artwork;
		_glyph = glyph;
		_time = time;
		_state = new UiState<SteamPlayingState>(Compute());
		_view = new UiView(surface, build(_state));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		if (_source is not null)
		{
			_source.Changed += OnChanged;
		}

		_artwork.Changed += OnChanged;
		Refresh();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		_view.Dispatch(uiEvent);
	}

	public ValueTask DisposeAsync()
	{
		if (_source is not null)
		{
			_source.Changed -= OnChanged;
		}

		_artwork.Changed -= OnChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnChanged(object? sender, EventArgs args)
	{
		Refresh();
	}

	private SteamPlayingState Compute()
	{
		return SteamWidgetStates.Playing(_source, _options, _time.GetUtcNow(), _artwork, _glyph);
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = Compute();
			if (next != _state.Value)
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}

			_timer.Change(next.Elapsed.Length > 0 ? _tick : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		}
	}
}
