using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class WeatherContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Weather,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((IWeatherProvider)integration).GetInstances().Select(i => i.Id), Does.Contain("berlin"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var station = new TestWeatherStation
		{
			SnapshotToReturn = new WeatherSnapshot
			{
				IsAvailable = true,
				LocationName = "Berlin, Germany",
				Temperature = 18.5,
				Condition = WeatherCondition.PartlyCloudy,
				Unit = TemperatureUnit.Celsius,
				Days = [new WeatherForecastDay(new DateOnly(2026, 8, 11), WeatherCondition.Rain, 10, 20)]
			}
		};
		var integration = await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = station })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var provider = (IWeatherProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(provider.ProviderName, Is.EqualTo("Open-Meteo"));
			Assert.That(provider.GetInstances().Select(i => i.Id), Is.EqualTo(new[] { "berlin" }));
		});

		var remoteStation = provider.GetStation("berlin");
		Assert.That(remoteStation, Is.Not.Null);

		var snapshot = await remoteStation!.GetSnapshotAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(snapshot.LocationName, Is.EqualTo("Berlin, Germany"));
			Assert.That(snapshot.Condition, Is.EqualTo(WeatherCondition.PartlyCloudy));
			Assert.That(snapshot.Days.Single().Condition, Is.EqualTo(WeatherCondition.Rain));
		});
	}

	[Test]
	public async Task The_instances_operation_round_trips_the_same_instance_list_describe_carries()
	{
		await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var raw = await InvokeRawAsync(CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Instances);
		var result = raw!.Value.Deserialize<WeatherInstancesResult>(PluginProtocolJson.Options);

		Assert.That(result!.Instances.Select(i => i.Id), Is.EqualTo(new[] { "berlin" }));
	}

	[Test]
	public async Task Notifying_a_stale_catalogue_makes_the_host_adapter_report_the_new_instance_list()
	{
		var stations = new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() };
		var integration = await ConnectAsync(
			[new WeatherCapabilityHandler([new TestWeatherIntegration("Open-Meteo", stations)], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.Weather]);

		var provider = (IWeatherProvider)integration;
		Assert.That(provider.GetInstances().Select(i => i.Id), Is.EqualTo(new[] { "berlin" }));

		stations["vienna"] = new TestWeatherStation();

		await SendStateUpdateFromPluginAsync(CapabilityKinds.Weather);

		await WaitForAsync(() => CurrentWeatherProvider().GetInstances().Count == 2,
			"The host's snapshot was never refreshed after state.update.");

		Assert.That(CurrentWeatherProvider().GetInstances().Select(i => i.Id),
			Is.EquivalentTo(new[] { "berlin", "vienna" }));

		IWeatherProvider CurrentWeatherProvider()
			=> (IWeatherProvider)IntegrationRegistry.Integrations
				.Single(candidate => string.Equals(candidate.Id, PluginId, StringComparison.Ordinal));
	}

	[Test]
	public async Task An_unknown_instance_id_resolves_to_no_station()
	{
		var integration = await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		Assert.That(((IWeatherProvider)integration).GetStation("gone"), Is.Null);
	}

	[Test]
	public async Task A_timeout_degrades_to_an_unavailable_snapshot_never_an_exception()
	{
		var station = new TestWeatherStation { SnapshotOverride = NeverReplies };
		var integration = await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = station })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var remoteStation = ((IWeatherProvider)integration).GetStation("berlin")!;

		var snapshotTask = remoteStation.GetSnapshotAsync(CancellationToken.None);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var snapshot = await snapshotTask;

		Assert.That(snapshot.IsAvailable, Is.False);
	}

	private static async Task<WeatherSnapshot> NeverReplies(CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return WeatherSnapshot.Unavailable();
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_an_unavailable_snapshot_never_an_exception()
	{
		var station = new TestWeatherStation();
		var integration = await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = station })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var remoteStation = ((IWeatherProvider)integration).GetStation("berlin")!;

		Disconnect();

		var snapshot = await remoteStation.GetSnapshotAsync(CancellationToken.None);
		Assert.That(snapshot.IsAvailable, Is.False);
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var station = new TestWeatherStation { SnapshotOverride = NeverReplies };
		await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = station })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		using var cts = new CancellationTokenSource();
		var invokeTask = InvokeRawAsync(CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Snapshot,
			new { instanceId = "berlin" },
			cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await invokeTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		await ConnectAsync([
				new WeatherCapabilityHandler([new ThrowingWeatherIntegration("Open-Meteo")], TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Snapshot,
			new { instanceId = "berlin" }));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("boom"));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() })
					],
					TestMetadata.Default)
			],
			[Provider()],
			[CapabilityKinds.Weather]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Weather,
			"nope",
			CapabilityOperations.Weather.Snapshot,
			new { instanceId = "berlin" }));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () => await InvokeRawAsync(
			CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			"rewind",
			new { instanceId = "berlin" }));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private sealed class ThrowingWeatherIntegration(string providerName) : IPluginIntegration, IWeatherProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName { get; } = providerName;

		public IReadOnlyList<WeatherStationInstance> GetInstances() => [new("berlin", "Berlin")];

		public IWeatherStation? GetStation(string instanceId) => new ThrowingStation();

		private sealed class ThrowingStation : IWeatherStation
		{
			public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct)
				=> throw new InvalidOperationException("boom: token=abc123");
		}
	}
}
