using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Weather;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using Serilog;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;

/// <summary>
/// The well-behaved counterpart to <c>MisbehavingIntegration</c>: one integration exercising actions
/// (plain, slider, dynamic options, one also configurable through a UI tree), variables, events, a
/// config flow (also served as a UI tree) and a provider capability (weather), so a suite that needs a
/// plugin doing everything right has one to point at. The
/// conformance suite runs against it in all three subject kinds, and
/// <c>MacroDeckHost.Tests.PluginContractTests</c> wires it straight into the host-side adapters.
///
/// <para>
/// Every piece is wired to the others rather than standing alone, which is what makes the
/// cross-capability assertions (an action raising an event that carries the weather reading a variable
/// also reports) possible. The teaching version of this plugin lives in the
/// <see href="https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins">sample plugins
/// repository</see>; keep behaviour changes here in sync with it.
/// </para>
/// </summary>
public sealed class WellBehavedIntegration : IPluginIntegration, IVariableProvider, IEventProvider,
	IUiConfigFlowProvider, IWeatherProvider
{
	/// <summary>The fixture's one weather station instance id - see <see cref="GetInstances"/>.</summary>
	internal const string StationId = "primary";

	internal const string WeatherRefreshedEventId = "weather-refreshed";

	/// <summary>The bounds of <see cref="AlertThresholdCelsius"/>, reported as the writable variable's
	/// volatile range and reused as the action parameter's slider range.</summary>
	internal const double AlertThresholdMin = -10;

	internal const double AlertThresholdMax = 40;

	private const string AlertThresholdVariableId = "alert-threshold-celsius";

	/// <summary>
	/// The conditions the "force weather condition" action lets a user pick, out of every value
	/// <see cref="WeatherCondition"/> declares - curated down to a handful so a demo deck's picker
	/// stays short rather than listing all thirteen.
	/// </summary>
	internal static readonly IReadOnlyList<WeatherCondition> SelectableConditions =
	[
		WeatherCondition.Clear, WeatherCondition.PartlyCloudy, WeatherCondition.Overcast,
		WeatherCondition.Rain, WeatherCondition.Thunderstorm, WeatherCondition.Snow
	];

	private readonly IPluginCatalogNotifier _catalogNotifier;
	private readonly ILogger _logger;

	private IIntegrationContext? _context;

	// Constructor injection, the same as IPluginCatalogNotifier above: MacroDeck.Plugin.Serilog's
	// UseMacroDeckLogging() (see Program.cs) is what routes this Serilog ILogger - and
	// MacroDeck.Sdk.Logging's IntegrationLog, and the static Log APIs - to the host's log viewer over
	// log.publish. ForContext is what gives every line below this integration's own source context;
	// the plugin id the host files it under is stamped host-side and cannot be set from here.
	public WellBehavedIntegration(IPluginCatalogNotifier catalogNotifier, ILogger logger)
	{
		_catalogNotifier = catalogNotifier;
		_logger = logger.ForContext<WellBehavedIntegration>();
		Station = new WellBehavedWeatherStation(this);
		Actions =
		[
			new RefreshWeatherAction(this), new SetAlertThresholdAction(this), new SetConditionAction(this),
			new ToggleStationPowerAction(this), new ReportStationIconAction()
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	/// <summary>The current location name, shown in the weather snapshot and the location variable.
	/// Defaults so the fixture already has something plausible to show before it is ever configured.</summary>
	internal string LocationName { get; private set; } = "Berlin, Germany";

	/// <summary>The threshold <see cref="WellBehavedWeatherStation.Tick"/> compares a fresh reading
	/// against. Written either through the "set alert threshold" action or straight through the
	/// <c>sample_alert_threshold_celsius</c> variable's write capability.</summary>
	internal double AlertThresholdCelsius { get; set; } = 30;

	/// <summary>Backs <see cref="ToggleStationPowerAction"/>'s state snapshot.</summary>
	internal bool StationPoweredOn { get; set; } = true;

	internal WellBehavedWeatherStation Station { get; }

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;

		// A real integration reads its config entries here, after the user has been through the flow
		// below - the same division of labor as SpotifyIntegration.ConnectFromConfig. The fixture keeps
		// to a single entry: the location name typed into the config flow's one field.
		var entries = await context.Config.GetEntriesAsync();
		if (entries.Count > 0)
		{
			var stored = await context.Config.GetStringAsync(entries[0].Id, WellBehavedConfigFlow.LocationFieldName);
			if (!string.IsNullOrWhiteSpace(stored))
			{
				LocationName = stored;
			}
		}

		// Seeds a first reading so the weather widget and the temperature variable already have
		// something to show before anyone presses "refresh weather".
		Station.Tick();

		_logger.Information("Initialized with location {Location} and alert threshold {ThresholdCelsius}°C.",
			LocationName,
			AlertThresholdCelsius);

		// The race this fixture exists to keep covered: the host sends weather's
		// describe concurrently with this method running, so a first-ever describe can win the race and
		// capture LocationName's Berlin default before the config read above ever ran - leaving the
		// weather widget's location dropdown (the host's stale snapshot) disagreeing with both the
		// widget itself and the config card (a live read and the just-submitted value). Telling the host
		// both catalogues are stale, now that LocationName is final, is what makes all three agree -
		// weather because GetInstances() below carries the location, variables because
		// "sample_location" does too.
		_catalogNotifier.CatalogChanged(CapabilityKinds.Weather, reason: "location config applied");
		_catalogNotifier.CatalogChanged(CapabilityKinds.Variables, reason: "location config applied");
	}

	public Task ShutdownAsync()
	{
		_context = null;
		return Task.CompletedTask;
	}

	// ----- IVariableProvider: the eager half only, polled by the host on each variable's own interval.
	// SupportsCatalog is left at its default, so this fixture gets no browse tree - the one thing every
	// plugin with a fixed set of variables should look like. -----

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("sample_location", VariableType.Text) with { Id = "location" },
		VariableDefinition.Eager("sample_temperature_celsius", VariableType.Numeric, decimalPlaces: 1) with
		{
			Id = "temperature-celsius"
		},

		// The writable one: a client may offer a control for this variable because the definition says so,
		// and the range a control needs travels with the reading rather than the declaration, because a
		// provider's bounds can move while its definition does not.
		VariableDefinition.Eager("sample_alert_threshold_celsius", VariableType.Numeric, decimalPlaces: 0) with
		{
			Id = AlertThresholdVariableId, Unit = "°C", Write = new VariableWriteCapability()
		}
	];

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(localId switch
		{
			"location" => VariableReading.Of(LocationName),
			"temperature-celsius" => VariableReading.Of(Station.LastSnapshot.Temperature),
			AlertThresholdVariableId => VariableReading.Of(AlertThresholdCelsius,
				AlertThresholdMin,
				AlertThresholdMax,
				step: 1),
			_ => VariableReading.Unavailable
		});

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (!string.Equals(localId, AlertThresholdVariableId, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableWriteResult.NotWritable());
		}

		if (value is not double threshold)
		{
			return ValueTask.FromResult(
				VariableWriteResult.InvalidValue("The alert threshold is a temperature in °C."));
		}

		AlertThresholdCelsius = Math.Clamp(threshold, AlertThresholdMin, AlertThresholdMax);
		return ValueTask.FromResult(VariableWriteResult.Applied());
	}

	// ----- IEventProvider: declares what this plugin can raise; publishing goes through IEventPublisher. -----
	// ProviderName is deliberately not implemented: the host falls back to the manifest name, so the one
	// place this plugin states its name stays manifest.json. Only a plugin exposing a differently-branded
	// provider needs to state one here.

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = WeatherRefreshedEventId,
			Name = "Weather refreshed",
			Description = "Raised whenever the fixture's synthetic weather reading changes.",
			PayloadParameters =
			[
				ActionParameter.Number("temperatureCelsius", "Temperature (°C)"),
				ActionParameter.Text("condition", "Condition"),
				ActionParameter.Toggle("isAlert", "Above alert threshold")
			]
		}
	];

	/// <summary>
	/// Called by <see cref="WellBehavedWeatherStation.Tick"/> after it computes a fresh reading.
	/// <see cref="IEventPublisher.Publish"/> is fire-and-forget by contract, so this mirrors that and
	/// never throws; <see cref="_context"/> is only null if something calls this before
	/// <see cref="InitializeAsync"/> has run or after <see cref="ShutdownAsync"/> has, neither of which
	/// a normally driven plugin process ever does.
	/// </summary>
	internal void PublishWeatherRefreshed(WeatherSnapshot snapshot, bool isAlert)
		=> _context?.Events.Publish(WeatherRefreshedEventId,
			new Dictionary<string, object?>
			{
				["temperatureCelsius"] = snapshot.Temperature,
				["condition"] = snapshot.Condition.ToString(),
				["isAlert"] = isAlert
			});

	// ----- IUiConfigFlowProvider (extends IConfigFlowProvider): ServesConfigUiTree defaults to true, so
	// declaring the interface is the whole opt-in - the flow itself is what renders the tree. -----

	public IConfigFlow CreateConfigFlow() => new WellBehavedConfigFlow();

	public bool AllowsMultipleConfigurations => false;

	// ----- IWeatherProvider: the provider capability, deliberately the synchronous-snapshot kind. -----

	public IReadOnlyList<WeatherStationInstance> GetInstances() =>
		[new(StationId, LocationName)];

	public IWeatherStation? GetStation(string instanceId)
		=> string.Equals(instanceId, StationId, StringComparison.Ordinal) ? Station : null;
}
