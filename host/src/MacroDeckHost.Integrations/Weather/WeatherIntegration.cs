using System.Globalization;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using Serilog;

namespace MacroDeckHost.Integrations.Weather;

[MacroDeckIntegration]
public sealed class WeatherIntegration
	: IIntegration, IWeatherProvider, IVariableProvider, IConfigFlowProvider, IIntegrationIconProvider, IDisposable
{
	private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(15);

	public const string IntegrationId = "app.macro-deck.weather";

	private static readonly ILogger _logger = IntegrationLog.For<WeatherIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly IOpenMeteoClient _client;

	public WeatherIntegration()
		: this(new OpenMeteoClient())
	{
	}

	internal WeatherIntegration(IOpenMeteoClient client)
	{
		_client = client;

		// Handed the live instance list rather than the integration itself, so the action reads the
		// stations as they are when a user opens its options rather than as they were at construction.
		Actions = [new WeatherDetailsActionDefinition(GetInstances)];
	}

	private volatile IReadOnlyList<ConfiguredStation> _stations = [];

	private CancellationTokenSource? _refreshCts;
	private Task? _refreshTask;

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Weather.Name();
	public string Version => "1.0.0";
	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string ProviderName => "Open-Meteo";

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("weather_temperature", VariableType.Numeric, 1, TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.TemperatureDisplayName()
			},
		VariableDefinition.Eager("weather_apparent_temperature", VariableType.Numeric, 1, TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.ApparentTemperatureDisplayName()
			},
		VariableDefinition.Eager("weather_condition", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.ConditionDisplayName()
			},
		VariableDefinition.Eager("weather_high", VariableType.Numeric, 1, TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.HighDisplayName()
			},
		VariableDefinition.Eager("weather_low", VariableType.Numeric, 1, TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.LowDisplayName()
			},
		VariableDefinition.Eager("weather_is_day", VariableType.Boolean, refreshInterval: TimeSpan.FromMinutes(10))
			with
			{
				DisplayName = AppStrings.Integrations.Weather.Variables.IsDayDisplayName()
			}
	];

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new WeatherConfigFlow(_client);

	public IReadOnlyList<WeatherStationInstance> GetInstances()
		=> _stations.Select(s => new WeatherStationInstance(s.EntryId, s.Station.DisplayName)).ToList();

	public IWeatherStation? GetStation(string instanceId)
		=> _stations.FirstOrDefault(s => s.EntryId == instanceId)?.Station;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await LoadStations(context);
		StartRefreshLoop();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		if (_refreshCts is not null)
		{
			await _refreshCts.CancelAsync();
		}

		if (_refreshTask is not null)
		{
			try
			{
				await _refreshTask;
			}
			catch (OperationCanceledException)
			{
			}
		}

		_refreshCts?.Dispose();
		_refreshCts = null;
		_refreshTask = null;
		IsInitialized = false;

		// Deliberately keep _stations as last-known-good rather than blanking it to []. A reinit runs
		// ShutdownAsync then InitializeAsync -> LoadStations, and blanking here opened a window where the
		// broadcast loop enumerated zero stations and pushed an empty list, flipping the widget to
		// "No location" until the next 60s tick (issue #94). LoadStations swaps in the rebuilt list
		// atomically; a genuinely disabled integration is already excluded by the registry.
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var stations = _stations;
		var snapshot = stations.Count > 0 ? stations[0].Station.Current : null;
		if (snapshot is null || !snapshot.IsAvailable)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var today = snapshot.Days.Count > 0 ? snapshot.Days[0] : null;

		object? value = localId switch
		{
			"weather-temperature" => snapshot.Temperature,
			"weather-apparent-temperature" => snapshot.ApparentTemperature,
			"weather-condition" => snapshot.Condition.ToString(),
			"weather-high" => today?.Max,
			"weather-low" => today?.Min,
			"weather-is-day" => snapshot.IsDay,
			_ => null
		};

		return ValueTask.FromResult(VariableReading.Of(value));
	}

	private async Task LoadStations(IIntegrationContext context)
	{
		var entries = await context.Config.GetEntriesAsync();
		var existing = _stations.ToDictionary(s => s.EntryId, StringComparer.Ordinal);
		var stations = new List<ConfiguredStation>();

		foreach (var entry in entries)
		{
			var latRaw = await context.Config.GetStringAsync(entry.Id, WeatherConfigKeys.Latitude);
			var lonRaw = await context.Config.GetStringAsync(entry.Id, WeatherConfigKeys.Longitude);
			var unitRaw = await context.Config.GetStringAsync(entry.Id, WeatherConfigKeys.Unit);
			var displayName = await context.Config.GetStringAsync(entry.Id, WeatherConfigKeys.DisplayName);

			if (!double.TryParse(latRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var latitude) ||
				!double.TryParse(lonRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var longitude))
			{
				_logger.Warning("Weather config entry {EntryId} has invalid coordinates; skipping", entry.Id);
				continue;
			}

			var unit = string.Equals(unitRaw, "fahrenheit", StringComparison.OrdinalIgnoreCase)
				? TemperatureUnit.Fahrenheit
				: TemperatureUnit.Celsius;

			var entryId = entry.Id.ToString();
			var name = string.IsNullOrWhiteSpace(entry.Title) ? displayName ?? string.Empty : entry.Title.Trim();

			existing.TryGetValue(entryId, out var prior);

			if (prior is not null && prior.Station.Matches(latitude, longitude, unit, name))
			{
				stations.Add(prior);
				continue;
			}

			// Carried over only when the location itself is unchanged: seeding a moved station would report
			// the previous location's weather, and a provider outage would keep it doing so.
			var seed = prior is not null &&
				prior.Station.Matches(latitude, longitude, unit, prior.Station.DisplayName)
					? prior.Station.Current
					: null;

			stations.Add(new ConfiguredStation(entryId,
				new WeatherStation(_client, latitude, longitude, unit, name, seed)));
		}

		_stations = stations;
		_logger.Information("Weather integration loaded {Count} location(s)", stations.Count);
	}

	private void StartRefreshLoop()
	{
		_refreshCts = new CancellationTokenSource();
		_refreshTask = Task.Run(() => RefreshLoop(_refreshCts.Token));
	}

	private async Task RefreshLoop(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(_refreshInterval);
		try
		{
			do
			{
				await RefreshAll(ct);
			} while (await timer.WaitForNextTickAsync(ct));
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task RefreshAll(CancellationToken ct)
	{
		foreach (var station in _stations)
		{
			try
			{
				await station.Station.RefreshAsync(ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to refresh weather for {Location}", station.Station.DisplayName);
			}
		}
	}

	public void Dispose()
	{
		_refreshCts?.Dispose();
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(WeatherIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("weather-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private sealed record ConfiguredStation(string EntryId, WeatherStation Station);
}
