using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;

namespace MacroDeckHost.Widgets.Weather;

internal sealed class WeatherWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly IWeatherStateNotifier _notifier;
	private readonly UiAsyncState<WeatherStatePayload> _state;
	private readonly string? _configuredInstanceId;

	public WeatherWidgetSession(UiView view,
		IWeatherStateNotifier notifier,
		UiAsyncState<WeatherStatePayload> state,
		string? configuredInstanceId)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(notifier);
		ArgumentNullException.ThrowIfNull(state);

		_view = view;
		_notifier = notifier;
		_state = state;
		_configuredInstanceId = configuredInstanceId;
		_notifier.StateChanged += OnWeatherStateChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_notifier.StateChanged -= OnWeatherStateChanged;

		return ValueTask.CompletedTask;
	}

	private void OnWeatherStateChanged(object? sender, WeatherStateChangedEventArgs args)
	{
		if (_configuredInstanceId is not null &&
			!string.Equals(_configuredInstanceId, args.InstanceId, StringComparison.Ordinal))
		{
			return;
		}

		_state.Reload();
	}
}
