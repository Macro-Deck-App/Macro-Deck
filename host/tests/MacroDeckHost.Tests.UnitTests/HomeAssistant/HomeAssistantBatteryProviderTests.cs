using System.Diagnostics;
using System.Text.Json;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Configuration;
using MacroDeckHost.Integrations.HomeAssistant.Connections;
using MacroDeckHost.Integrations.HomeAssistant.Providers;
using MacroDeckHost.Tests.UnitTests.Adb;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantBatteryProviderTests
{
	private const string PhoneBattery = "sensor.phone_battery";
	private const string PhoneCharging = "binary_sensor.phone_is_charging";
	private const string WatchBattery = "sensor.watch_battery";
	private const string TabletBattery = "sensor.tablet_battery";

	private static readonly Uri _uri = new("ws://homeassistant.local:8123/api/websocket");

	private static readonly string _states =
		"""
		[ { "entity_id": "sensor.phone_battery", "state": "57",
		    "attributes": { "device_class": "battery", "friendly_name": "Phone Battery" } },
		  { "entity_id": "binary_sensor.phone_is_charging", "state": "on",
		    "attributes": { "device_class": "battery_charging" } },
		  { "entity_id": "sensor.watch_battery", "state": "80",
		    "attributes": { "device_class": "battery", "friendly_name": "Watch Battery" } },
		  { "entity_id": "sensor.tablet_battery", "state": "33",
		    "attributes": { "device_class": "battery", "friendly_name": "Tablet Battery" } },
		  { "entity_id": "light.kitchen", "state": "on", "attributes": { "friendly_name": "Kitchen" } } ]
		""";

	private static readonly string _devices =
		"""
		[ { "id": "dev-phone", "name": "Pixel", "name_by_user": "Pixel 8" },
		  { "id": "dev-watch", "name": "Watch" } ]
		""";

	private static readonly string _entityRegistry =
		"""
		[ { "entity_id": "sensor.phone_battery", "device_id": "dev-phone" },
		  { "entity_id": "binary_sensor.phone_is_charging", "device_id": "dev-phone" },
		  { "entity_id": "sensor.watch_battery", "device_id": "dev-watch" },
		  { "entity_id": "light.kitchen", "device_id": null } ]
		""";

	private readonly List<FakeHomeAssistantClient> _clients = [];
	private readonly Queue<Action<FakeHomeAssistantClient>> _script = new();

	private HomeAssistantConnection? _connection;
	private HomeAssistantBatteryProvider _provider = null!;
	private RecordingBatteryProviderContext _batteries = null!;

	[SetUp]
	public void SetUp()
	{
		_clients.Clear();
		_script.Clear();
		_provider = new HomeAssistantBatteryProvider(() => _connection);
		_batteries = new RecordingBatteryProviderContext();
	}

	[TearDown]
	public void TearDown()
	{
		_connection?.Dispose();
		_provider.Dispose();
	}

	[Test]
	public async Task A_selected_sensor_is_published_under_its_device_name_with_level_and_charging_state()
	{
		await StartAsync([PhoneBattery]);

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		var published = _batteries.Latest(IdOf(PhoneBattery));
		Assert.Multiple(() =>
		{
			Assert.That(published,
				Is.EqualTo(new BatteryDevice
				{
					Id = IdOf(PhoneBattery),
					Name = "Pixel 8",
					Kind = BatteryDeviceKinds.Other,
					LevelPercent = 57,
					ChargeState = BatteryChargeState.Charging,
					IsConnected = true
				}));
			Assert.That(published!.Id, Does.Not.Contain(PhoneBattery).And.Not.Contain("phone_battery"));
		});
	}

	[Test]
	public async Task A_battery_sensor_the_user_did_not_pick_is_not_published()
	{
		await StartAsync([PhoneBattery]);

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		Assert.That(_batteries.Publishes.SelectMany(batch => batch).Select(device => device.Id),
			Has.None.EqualTo(IdOf(WatchBattery)));
	}

	[Test]
	public async Task A_sensor_without_a_device_uses_its_friendly_name_and_an_unknown_charge_state()
	{
		await StartAsync([TabletBattery]);

		await WaitForAsync(() => _batteries.Latest(IdOf(TabletBattery)) is { IsConnected: true }, "the tablet battery");

		Assert.Multiple(() =>
		{
			Assert.That(_batteries.Latest(IdOf(TabletBattery))!.Name, Is.EqualTo("Tablet Battery"));
			Assert.That(_batteries.Latest(IdOf(TabletBattery))!.ChargeState, Is.EqualTo(BatteryChargeState.Unknown));
		});
	}

	[Test]
	public async Task The_charging_sensor_turning_off_is_published_as_discharging()
	{
		await StartAsync([PhoneBattery]);
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		RaiseState(PhoneCharging, "off", """{ "device_class": "battery_charging" }""");

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery))?.ChargeState == BatteryChargeState.Discharging,
			"the unplugged phone");
	}

	[Test]
	public async Task A_level_change_of_a_selected_sensor_is_republished()
	{
		await StartAsync([PhoneBattery]);
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		RaiseState(PhoneBattery, "41.6", """{ "device_class": "battery" }""");

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery))?.LevelPercent == 42, "the new level");
	}

	[Test]
	public async Task The_connection_reports_state_changes_of_watched_battery_entities_only()
	{
		var changes = 0;
		_connection = new HomeAssistantConnection(CreateClient,
			_uri,
			"the-token",
			onBatteryRelevantChange: () => Interlocked.Increment(ref changes));
		_connection.WatchBatteryEntities(new HashSet<string>(StringComparer.Ordinal) { PhoneBattery });
		_connection.Start();
		await WaitForAsync(() => _connection.IsConnected && Volatile.Read(ref changes) > 0, "the connection");
		await Task.Delay(100);
		var before = Volatile.Read(ref changes);

		RaiseState("light.kitchen", "off", "{}");
		RaiseState(WatchBattery, "79", """{ "device_class": "battery" }""");
		RaiseState(PhoneBattery, "56", """{ "device_class": "battery" }""");
		await WaitForAsync(() => Volatile.Read(ref changes) > before, "the watched sensor's change");
		await Task.Delay(100);

		Assert.That(Volatile.Read(ref changes), Is.EqualTo(before + 1));
	}

	[Test]
	public async Task An_unavailable_sensor_is_not_connected_and_keeps_its_last_level()
	{
		await StartAsync([PhoneBattery]);
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		RaiseState(PhoneBattery, "unavailable", """{ "device_class": "battery" }""");

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: false },
			"the unavailable phone");
		Assert.That(_batteries.Latest(IdOf(PhoneBattery))!.LevelPercent, Is.EqualTo(57));
	}

	[Test]
	public async Task A_reading_outside_zero_to_hundred_has_no_level()
	{
		await StartAsync([PhoneBattery]);
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		RaiseState(PhoneBattery, "140", """{ "device_class": "battery" }""");

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { LevelPercent: null }, "the dropped level");
		Assert.That(_batteries.Latest(IdOf(PhoneBattery))!.IsConnected, Is.True);
	}

	[Test]
	public async Task Losing_home_assistant_shows_every_battery_as_not_connected()
	{
		await StartAsync([PhoneBattery, WatchBattery], TimeSpan.FromMinutes(10));
		await WaitForAsync(() => _batteries.Latest(IdOf(WatchBattery)) is { IsConnected: true }, "both batteries");

		_clients[^1].Drop();

		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: false }
				&& _batteries.Latest(IdOf(WatchBattery)) is { IsConnected: false },
			"both batteries to go offline");
		Assert.That(_batteries.Removed, Is.Empty);
	}

	[Test]
	public async Task A_sensor_missing_after_a_home_assistant_restart_stays_published_as_not_connected()
	{
		await StartAsync([PhoneBattery]);
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");
		_script.Enqueue(client => client.Responses["get_states"] = "[]");

		_clients[^1].Drop();
		await WaitForAsync(() => _clients.Count == 2 && _connection!.IsConnected, "the reconnect");
		await Task.Delay(200);

		Assert.Multiple(() =>
		{
			Assert.That(_batteries.Latest(IdOf(PhoneBattery)), Is.Not.Null);
			Assert.That(_batteries.Latest(IdOf(PhoneBattery))!.IsConnected, Is.False);
			Assert.That(_batteries.Removed, Is.Empty);
		});
	}

	[Test]
	public async Task A_late_callback_from_a_replaced_connection_is_ignored()
	{
		await StartAsync([PhoneBattery], TimeSpan.FromMinutes(10));
		await WaitForAsync(() => _batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		_provider.BeginConnection();
		_clients[^1].Drop();
		await WaitForAsync(() => !_connection!.IsConnected, "the dropped session");
		await Task.Delay(200);

		Assert.That(_batteries.Latest(IdOf(PhoneBattery))!.IsConnected, Is.True);
	}

	[Test]
	public async Task Reconfiguring_reconnects_and_removes_only_the_deselected_sensor()
	{
		using var integration = new HomeAssistantIntegration(CreateClient);
		var context = new FakeHomeAssistantIntegrationContext();
		var entryId = AddEntry(context, "http://homeassistant.local:8123", [PhoneBattery, WatchBattery]);
		await integration.InitializeAsync(context);
		var batteries = new RecordingBatteryProviderContext();
		await ((IBatteryProvider)integration).InitializeAsync(batteries);
		await WaitForAsync(() => batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }
				&& batteries.Latest(IdOf(WatchBattery)) is { IsConnected: true },
			"both batteries");

		await context.ConfigStore.SetStringAsync(entryId,
			HomeAssistantConfigKeys.BatteryEntities,
			$"[\"{WatchBattery}\"]");
		var reloaded = await ((IConfigEntryLifecycle)integration).ReloadEntriesAsync();
		await WaitForAsync(() => _clients.Count == 2
				&& _clients[^1].IsConnected
				&& batteries.Latest(IdOf(WatchBattery)) is { IsConnected: true },
			"the new connection to publish the kept sensor");

		Assert.Multiple(() =>
		{
			Assert.That(reloaded, Is.True);
			Assert.That(_clients[0].Disposed, Is.True);
			Assert.That(batteries.Removed, Is.EqualTo(new[] { IdOf(PhoneBattery) }));
			Assert.That(batteries.Latest(IdOf(WatchBattery))!.IsConnected, Is.True);
		});
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Reconfiguring_the_address_replaces_the_connection()
	{
		using var integration = new HomeAssistantIntegration(CreateClient);
		var context = new FakeHomeAssistantIntegrationContext();
		var entryId = AddEntry(context, "http://homeassistant.local:8123", [PhoneBattery]);
		await integration.InitializeAsync(context);
		var batteries = new RecordingBatteryProviderContext();
		await ((IBatteryProvider)integration).InitializeAsync(batteries);
		await WaitForAsync(() => batteries.Latest(IdOf(PhoneBattery)) is { IsConnected: true }, "the phone battery");

		await context.ConfigStore.SetStringAsync(entryId, HomeAssistantConfigKeys.BaseUrl, "http://ha.lan:8123");
		var reloaded = await ((IConfigEntryLifecycle)integration).ReloadEntriesAsync();
		await WaitForAsync(() => _clients.Count == 2 && _clients[^1].IsConnected, "the new connection");

		Assert.Multiple(() =>
		{
			Assert.That(reloaded, Is.True);
			Assert.That(_clients[0].Disposed, Is.True);
			Assert.That(_clients[^1].LastUri?.Host, Is.EqualTo("ha.lan"));
			Assert.That(batteries.Removed, Is.Empty);
		});
		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Reconfiguring_before_the_battery_provider_started_still_reloads_the_connection()
	{
		using var integration = new HomeAssistantIntegration(CreateClient);
		var context = new FakeHomeAssistantIntegrationContext();
		var entryId = AddEntry(context, "http://homeassistant.local:8123", [PhoneBattery]);
		await integration.InitializeAsync(context);

		await context.ConfigStore.SetStringAsync(entryId, HomeAssistantConfigKeys.BaseUrl, "http://ha.lan:8123");
		var reloaded = await ((IConfigEntryLifecycle)integration).ReloadEntriesAsync();
		// The first connection may be replaced before its pump ever creates a client, so count nothing.
		await WaitForAsync(() => ConnectedTo("ha.lan"), "the new connection");

		Assert.That(reloaded, Is.True);
		await integration.ShutdownAsync();
	}

	[Test]
	public void The_integration_offers_its_batteries_without_a_provider_label_of_its_own()
	{
		using var integration = new HomeAssistantIntegration(CreateClient);

		Assert.That(((IBatteryProvider)integration).ProviderName, Is.Empty);
	}

	private static Guid AddEntry(FakeHomeAssistantIntegrationContext context, string baseUrl, string[] batteries)
	{
		return context.ConfigStore.AddEntry("Home Assistant",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.BaseUrl] = baseUrl,
				[HomeAssistantConfigKeys.Token] = "the-token",
				[HomeAssistantConfigKeys.BatteryEntities] = JsonSerializer.Serialize(batteries)
			});
	}

	private bool ConnectedTo(string host)
	{
		lock (_clients)
		{
			return _clients.Any(c => c.IsConnected && string.Equals(c.LastUri?.Host, host, StringComparison.Ordinal));
		}
	}

	private static string IdOf(string entityId)
	{
		return HomeAssistantBatteryProvider.IdFor(entityId);
	}

	private static async Task WaitForAsync(Func<bool> condition, string because, int timeout = 5)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(timeout))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private async Task StartAsync(IReadOnlyList<string> selection, TimeSpan? reconnectDelay = null)
	{
		var generation = _provider.BeginConnection();
		_connection = new HomeAssistantConnection(CreateClient,
			_uri,
			"the-token",
			reconnectDelay: reconnectDelay ?? TimeSpan.FromMilliseconds(20),
			onBatteryRelevantChange: () => _provider.ConnectionChanged(generation));
		await _provider.InitializeAsync(_batteries, CancellationToken.None);
		await _provider.UpdateSelectionAsync(selection, CancellationToken.None);
		_connection.Start();
	}

	private FakeHomeAssistantClient CreateClient()
	{
		var client = new FakeHomeAssistantClient
		{
			Responses =
			{
				["get_states"] = _states,
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }""",
				["get_services"] = "{}",
				["config/area_registry/list"] = "[]",
				["config/device_registry/list"] = _devices,
				["config/entity_registry/list"] = _entityRegistry
			}
		};

		if (_script.Count > 0)
		{
			_script.Dequeue().Invoke(client);
		}

		lock (_clients)
		{
			_clients.Add(client);
		}

		return client;
	}

	private void RaiseState(string entityId, string state, string attributes)
	{
		_clients[^1]
			.RaiseEvent("state_changed",
				$$"""
				  { "entity_id": "{{entityId}}",
				    "new_state": { "entity_id": "{{entityId}}", "state": "{{state}}", "attributes": {{attributes}} } }
				  """);
	}
}
