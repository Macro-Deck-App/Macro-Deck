using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Hosting.Capabilities.Icons;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class WellBehavedPluginContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Action(string localId) => new()
	{
		Kind = CapabilityKinds.Actions, LocalId = localId,
		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
	};

	private static DeclaredCapability Variable(string localId) => new()
	{
		Kind = CapabilityKinds.Variables, LocalId = localId,
		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
	};

	private static DeclaredCapability Provider(string kind) => new()
	{
		Kind = kind, LocalId = ProviderCapabilityId.LocalId,
		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
	};

	private static DeclaredCapability Icon() => new()
	{
		Kind = CapabilityKinds.Icons, LocalId = IconsCapabilityHandler.LocalId,
		VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
	};

	[Test]
	public async Task The_fixture_declares_every_capability_it_promises_and_each_kind_round_trips()
	{
		var events = new RecordingEventPublisher();
		var config = new FakeIntegrationConfig(new Dictionary<string, string?> { ["location"] = "Vienna, Austria" });
		var catalogNotifier = new RecordingCatalogNotifier();
		var fixture = new WellBehavedIntegration(catalogNotifier, Serilog.Core.Logger.None);

		await fixture.InitializeAsync(new FakeIntegrationContext(config, events));

		// Regression for issue #413's remote weather-location bug: InitializeAsync must tell the host
		// both catalogues its config just changed are stale, or a describe that raced ahead of the config
		// read above would leave the host's snapshot on the Berlin default forever.
		Assert.Multiple(() =>
		{
			Assert.That(catalogNotifier.Changed,
				Has.Some.Matches<(string Kind, string? LocalId)>(call => call.Kind == CapabilityKinds.Weather));
			Assert.That(catalogNotifier.Changed,
				Has.Some.Matches<(string Kind, string? LocalId)>(call => call.Kind == CapabilityKinds.Variables));
		});

		var iconBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.svg"));

		var configFlowSessions = new PluginConfigFlowSessions(TimeProvider.System);

		var integration = await ConnectAsync([
				new ActionsCapabilityHandler([fixture]),
				new VariablesCapabilityHandler([fixture],
					TestMetadata.Default,
					new VariableSubscriptions(Serilog.Log.Logger),
					Serilog.Core.Logger.None),
				new EventsCapabilityHandler([fixture], TestMetadata.Default),
				new ConfigFlowCapabilityHandler([fixture], configFlowSessions),
				new WeatherCapabilityHandler([fixture], TestMetadata.Default),
				new IconsCapabilityHandler(new IconAssetSource(iconBytes, "image/svg+xml")),
				new UiCapabilityHandler([fixture],
					CreatePluginHostInvoker(),
					Serilog.Core.Logger.None,
					configFlowSessions,
					new ModalResultStore())
			],
			[
				Action("refresh-weather"), Action("set-alert-threshold"), Action("set-condition"),
				Variable("location"), Variable("temperature-celsius"),
				Provider(CapabilityKinds.Events),
				Provider(CapabilityKinds.ConfigFlow),
				Provider(CapabilityKinds.Weather),
				Icon(),
				Provider(CapabilityKinds.Ui)
			],
			[
				CapabilityKinds.Actions, CapabilityKinds.Variables, CapabilityKinds.Events,
				CapabilityKinds.ConfigFlow, CapabilityKinds.Weather, CapabilityKinds.Icons, CapabilityKinds.Ui
			],
			beforeRegister: () => UploadAssetAsync(AssetKinds.Icon, "image/svg+xml", iconBytes));

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(integration, Is.InstanceOf<IVariableProvider>());
			Assert.That(integration, Is.InstanceOf<IEventProvider>());
			Assert.That(integration, Is.InstanceOf<IConfigFlowProvider>());
			Assert.That(integration, Is.InstanceOf<IWeatherProvider>());
			Assert.That(integration, Is.InstanceOf<IIntegrationIconProvider>());
		});

		var refresh = integration.Actions.Single(a => a.Id == "refresh-weather");
		var refreshResult = await refresh.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
		Assert.That(refreshResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));

		var thresholdResult = await integration.Actions.Single(a => a.Id == "set-alert-threshold").CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
				{ Parameters = new Dictionary<string, object> { ["thresholdCelsius"] = 10.0 } });
		Assert.That(thresholdResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));

		var conditionDefinition
			= (IDynamicOptionsActionDefinition)integration.Actions.Single(a => a.Id == "set-condition");
		var options = await conditionDefinition.GetDynamicOptionsAsync(new DynamicOptionsContext
				{ ParameterName = "condition", CurrentParameters = new Dictionary<string, object?>() },
			CancellationToken.None);
		var conditionResult = await integration.Actions.Single(a => a.Id == "set-condition").CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["condition"] = options.Options[0].Value }
			});
		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Is.Not.Empty);
			Assert.That(conditionResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});

		var variableProvider = (IVariableProvider)integration;
		var locationValue = (await variableProvider.ReadAsync("location", CancellationToken.None)).Value;
		var temperatureValue
			= (await variableProvider.ReadAsync("temperature-celsius", CancellationToken.None)).Value;
		Assert.Multiple(() =>
		{
			Assert.That(locationValue, Is.EqualTo("Vienna, Austria"));
			Assert.That(temperatureValue, Is.Not.Null);
		});

		var flowProvider = (IConfigFlowProvider)integration;
		var flow = flowProvider.CreateConfigFlow();
		var flowContext = new TestConfigFlowContext();
		var start = await flow.StartAsync(flowContext, CancellationToken.None);
		var completed = await flow.SubmitAsync(start.NextStep!.StepId,
			new Dictionary<string, object?> { ["location"] = "Vienna, Austria" },
			flowContext,
			CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(start.NextStep!.Fields.Single().Name, Is.EqualTo("location"));
			Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		});

		var weatherProvider = (IWeatherProvider)integration;
		var stationInstance = weatherProvider.GetInstances().Single();
		var station = weatherProvider.GetStation(stationInstance.Id);
		var snapshot = await station!.GetSnapshotAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(stationInstance.DisplayName, Is.EqualTo("Vienna, Austria"));
			Assert.That(snapshot.IsAvailable, Is.True);
			Assert.That(snapshot.LocationName, Is.EqualTo("Vienna, Austria"));
		});

		var iconProvider = (IIntegrationIconProvider)integration;
		Assert.That(iconProvider.GetIcon(), Is.EqualTo(iconBytes));

		Assert.That(events.Occurrences,
			Has.Some.Matches<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)>(occurrence =>
				occurrence.EventId == WellBehavedIntegration.WeatherRefreshedEventId));
	}

	private sealed class RecordingCatalogNotifier : IPluginCatalogNotifier
	{
		public List<(string Kind, string? LocalId)> Changed { get; } = [];

		public void CatalogChanged(string kind, string? localId = null, string? reason = null)
			=> Changed.Add((kind, localId));
	}

	private sealed class RecordingEventPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Occurrences { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Occurrences.Add((eventId, parameters));
	}

	private sealed class FakeIntegrationConfig(IReadOnlyDictionary<string, string?> values) : IIntegrationConfig
	{
		private readonly Guid _entryId = Guid.CreateVersion7();
		private readonly Dictionary<string, string?> _values = new(values, StringComparer.Ordinal);

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([new ConfigEntrySnapshot(_entryId, "Sample")]);

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(_values.GetValueOrDefault(key));

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(null);

		public Task SetStringAsync(Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
		{
			_values[key] = value;
			return Task.CompletedTask;
		}

		public Task SetSecretAsync(Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class FakeIntegrationContext(IIntegrationConfig config, IEventPublisher events) : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException("Not used by the fixture.");

		public IUserVariableApi UserVariables => throw new NotSupportedException("Not used by the fixture.");

		public IIntegrationConfig Config { get; } = config;

		public IDeckNavigator Deck => throw new NotSupportedException("Not used by the fixture.");

		public IScriptApi Scripts => throw new NotSupportedException("Not used by the fixture.");

		public IWidgetApi Widgets => throw new NotSupportedException("Not used by the fixture.");

		public IEventPublisher Events { get; } = events;

		public IUserNotifier Notifications => throw new NotSupportedException("Not used by the fixture.");
	}
}
