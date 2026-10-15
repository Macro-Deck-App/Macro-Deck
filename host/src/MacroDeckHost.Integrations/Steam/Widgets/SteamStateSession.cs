using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed class SteamStateSession<TState> : IUiSession
	where TState : class
{
	private readonly ISteamSource? _source;
	private readonly SteamArtworkCache _artwork;
	private readonly Func<TState> _compute;
	private readonly UiState<TState> _state;
	private readonly UiView _view;
	private readonly Lock _sync = new();
	private bool _disposed;

	public SteamStateSession(
		UiSurface surface,
		ISteamSource? source,
		SteamArtworkCache artwork,
		Func<TState> compute,
		Func<UiState<TState>, UiElement> build)
	{
		_source = source;
		_artwork = artwork;
		_compute = compute;
		_state = new UiState<TState>(compute());
		_view = new UiView(surface, build(_state));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

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

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnChanged(object? sender, EventArgs args)
	{
		Refresh();
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = _compute();
			if (!next.Equals(_state.Value))
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}
		}
	}
}
