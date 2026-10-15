using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal sealed class WeatherDetailsSession : IUiSession
{
	private readonly IWeatherStations _stations;
	private readonly UiState<WeatherDetailsState> _state;
	private readonly UiState<WeatherDetailsIcons> _icons;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private WeatherDetailsStation _station;
	private Func<IUiResourceRegistry?>? _pendingIcons;
	private bool _disposed;

	public WeatherDetailsSession(
		UiSurface surface,
		IWeatherStations stations,
		WeatherDetailsStation station,
		WeatherDetailsIcons icons,
		Func<IUiResourceRegistry?>? pendingIcons,
		WeatherDetailsTimeFormat? timeFormat)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(stations);

		_stations = stations;
		_station = station;
		_pendingIcons = pendingIcons;
		_icons = new UiState<WeatherDetailsIcons>(icons);
		_state = new UiState<WeatherDetailsState>(Resolve());
		_view = new UiView(surface,
			WeatherDetailsView.Build(_state, _icons, timeFormat is null ? null : timeFormat.Format));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_stations.StationsChanged += OnStationsChanged;
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
		_stations.StationsChanged -= OnStationsChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private WeatherDetailsState Resolve()
	{
		if (_station.IsForeign)
		{
			return WeatherDetailsState.UnknownStation();
		}

		if (_station.StationId is null && _stations.FirstStationId is { } first)
		{
			_station = WeatherDetailsStation.Of(first);
		}

		return _station.StationId is { } stationId && _stations.Snapshot(stationId) is { } snapshot
			? WeatherDetailsState.From(snapshot)
			: WeatherDetailsState.UnknownStation();
	}

	private void OnStationsChanged(object? sender, EventArgs args)
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			using (_view.Batch())
			{
				_state.Set(Resolve());
			}

			if (_pendingIcons?.Invoke() is { } registry)
			{
				_pendingIcons = null;
				_ = ResolveIconsAsync(registry);
			}
		}
	}

	private async Task ResolveIconsAsync(IUiResourceRegistry registry)
	{
		var icons = await WeatherDetailsIcons.ResolveAsync(registry, CancellationToken.None).ConfigureAwait(false);

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			using (_view.Batch())
			{
				_icons.Set(icons);
			}
		}
	}
}
