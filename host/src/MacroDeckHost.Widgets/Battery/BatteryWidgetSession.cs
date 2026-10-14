using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Battery;

internal sealed class BatteryWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<BatteryViewState> _state;
	private readonly BatteryWidgetData _config;
	private readonly BatteryViewStateResolver _resolver;
	private readonly IBatteryApi _batteries;

	// Changed arrives on the registry's thread while a client drains this view; resolving under the lock
	// keeps two changes from landing out of order and a late one from writing into a disposed view.
	private readonly Lock _sync = new();
	private bool _disposed;

	public BatteryWidgetSession(
		UiView view,
		UiState<BatteryViewState> state,
		BatteryWidgetData config,
		BatteryViewStateResolver resolver,
		IBatteryApi batteries)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(batteries);

		_view = view;
		_state = state;
		_config = config;
		_resolver = resolver;
		_batteries = batteries;

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_batteries.Changed += OnBatteriesChanged;
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
		_view.Dispatch(uiEvent);
	}

	public ValueTask DisposeAsync()
	{
		_batteries.Changed -= OnBatteriesChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		return ValueTask.CompletedTask;
	}

	private void OnBatteriesChanged(object? sender, BatteryDevicesChangedEventArgs args)
	{
		lock (_sync)
		{
			if (!_disposed)
			{
				_state.Set(_resolver.Resolve(_config));
			}
		}
	}
}
