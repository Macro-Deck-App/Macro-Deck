using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ping.Connections;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal sealed class PingWidgetSession : IUiSession
{
	private readonly PingWidgetOptions _options;
	private readonly IPingTargets _targets;
	private readonly UiState<PingViewState> _state;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private bool _disposed;

	public PingWidgetSession(
		UiSurface surface,
		PingWidgetOptions options,
		IPingTargets targets,
		string? backgroundColor)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(targets);

		_options = options;
		_targets = targets;
		_state = new UiState<PingViewState>(PingViewStateResolver.Resolve(options, targets));
		_view = new UiView(surface,
			PingWidgetView.Build(_state, UiWidgetSafeArea.RadiusOf(surface), backgroundColor));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
		_targets.Changed += OnTargetsChanged;
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
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	public ValueTask DisposeAsync()
	{
		_targets.Changed -= OnTargetsChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	private void OnTargetsChanged(object? sender, EventArgs args)
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = PingViewStateResolver.Resolve(_options, _targets);
			if (next == _state.Value)
			{
				return;
			}

			using (_view.Batch())
			{
				_state.Set(next);
			}
		}
	}
}
