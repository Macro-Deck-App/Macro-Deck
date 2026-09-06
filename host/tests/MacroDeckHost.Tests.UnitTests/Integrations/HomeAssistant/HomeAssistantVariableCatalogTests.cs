using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Integrations.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantVariableCatalogTests
{
	private static readonly string[] _singleWatchedEntity = ["light.a"];

	[Test]
	public async Task Every_attribute_of_an_entity_is_discoverable()
	{
		var catalog = CatalogOf(State("light.living_room",
			"on",
			("brightness", 128),
			("color_temp", 370),
			("friendly_name", "Living Room")));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var page = await provider.DiscoverAsync(new VariableCatalogQuery { ParentId = "entity/light.living_room" });

		Assert.That(page.Items.Select(item => item.DisplayName.Literal),
			Is.EquivalentTo(["state", "brightness", "color_temp", "friendly_name", "attributes"]));
	}

	[Test]
	public async Task An_entity_state_and_its_attributes_get_the_types_a_consumer_needs()
	{
		var catalog = CatalogOf(State("light.living_room", "on", ("brightness", 128), ("friendly_name", "Living Room")),
			State("sensor.office_temperature", "21.5", ("unit_of_measurement", "°C")),
			State("switch.desk", "on"));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var temperature = await provider.ResolveAsync("entity/sensor.office_temperature/state");
		var brightness = await provider.ResolveAsync("entity/light.living_room/brightness");
		var friendlyName = await provider.ResolveAsync("entity/light.living_room/friendly_name");
		var deskSwitch = await provider.ResolveAsync("entity/switch.desk/state");

		Assert.Multiple(() =>
		{
			// These two prove types are not all collapsing to Text.
			Assert.That(temperature?.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(brightness?.Type, Is.EqualTo(VariableType.Numeric));

			Assert.That(friendlyName?.Type, Is.EqualTo(VariableType.Text));

			// Not Boolean: VariableValueSerializer only treats "true"/"1" as truthy for a Boolean variable,
			// so a "switch"/"light" state of "on" would silently serialize as "false". Text is the only
			// default type that cannot mis-render it; the user can still override the type at bind time.
			Assert.That(deskSwitch?.Type, Is.EqualTo(VariableType.Text));
		});
	}

	[Test]
	public async Task An_on_off_entity_states_raw_value_is_exposed_as_is()
	{
		// ReadValue must hand back exactly what Home Assistant reported for the state, not a coerced bool -
		// otherwise a Text-typed variable (which is what every migrated ha_* variable is; see
		// HomeAssistantWatchedEntityMigration) would serialize "True"/"False" instead of "on"/"off", and
		// every existing `{{ vars.ha_switch_desk == "on" }}` template would silently break.
		var catalog = CatalogOf(State("switch.desk", "on"), State("light.living_room", "off"));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var deskSwitch = (await provider.ReadAsync("entity/switch.desk/state")).Value;
		var livingRoomLight = (await provider.ReadAsync("entity/light.living_room/state")).Value;

		Assert.Multiple(() =>
		{
			Assert.That(deskSwitch, Is.EqualTo("on"));
			Assert.That(livingRoomLight, Is.EqualTo("off"));
		});
	}

	[Test]
	public async Task An_entity_variable_keeps_the_name_it_had_before_the_migration()
	{
		var watchedEntities = new List<string> { "light.living_room", "sensor.office_temperature" };
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(watchedEntities));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		var bindings = await migration.BuildAsync([]);
		var livingRoomState = bindings.Single(b => b.LocalResourceId == "entity/light.living_room/state");
		var livingRoomAttributes = bindings.Single(b => b.LocalResourceId == "entity/light.living_room/attributes");
		var temperatureState = bindings.Single(b => b.LocalResourceId == "entity/sensor.office_temperature/state");

		Assert.Multiple(() =>
		{
			Assert.That(bindings, Has.Count.EqualTo(4));

			Assert.That(livingRoomState.Name, Is.EqualTo("ha_light_living_room"));
			Assert.That(livingRoomAttributes.Name, Is.EqualTo("ha_light_living_room_attributes"));
			Assert.That(temperatureState.Name, Is.EqualTo("ha_sensor_office_temperature"));

			Assert.That(bindings.Select(b => b.Type), Has.All.EqualTo(Domain.Enums.VariableType.Text));
			Assert.That(bindings.Select(b => b.IntegrationId),
				Has.All.EqualTo(HomeAssistantIntegration.IntegrationId));
			Assert.That(bindings.Select(b => b.MigratedFrom),
				Has.All.EqualTo(HomeAssistantWatchedEntityMigration.MigratedFromId));
		});
	}

	[Test]
	public async Task Two_entity_ids_that_sanitize_to_the_same_core_get_a_dash_2_suffix()
	{
		// "sensor.a!b" and "sensor.a#b" both sanitize to "sensor-a-b" - Core() collapses any run of
		// non-alphanumeric characters to a single '-'. The second one bound must not silently overwrite or
		// collide with the first's variable name.
		var watchedEntities = new List<string> { "sensor.a!b", "sensor.a#b" };
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(watchedEntities));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		var bindings = await migration.BuildAsync([]);

		var first = bindings.Single(b => b.LocalResourceId == "entity/sensor.a!b/state");
		var second = bindings.Single(b => b.LocalResourceId == "entity/sensor.a#b/state");

		Assert.Multiple(() =>
		{
			Assert.That(first.Name, Is.EqualTo("ha_sensor_a_b"));
			Assert.That(second.Name, Is.EqualTo("ha_sensor_a_b_2"));
		});
	}

	[Test]
	public async Task An_entity_id_longer_than_the_declared_id_limit_gets_truncated_with_a_hash_tail()
	{
		var longEntityId = "sensor." + new string('a', 60);
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(new List<string> { longEntityId }));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		var bindings = await migration.BuildAsync([]);
		var state = bindings.Single(b => b.LocalResourceId == $"entity/{longEntityId}/state");

		Assert.That(state.Name, Is.EqualTo("ha_sensor_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_16c3803b"));
	}

	[Test]
	public async Task An_entity_id_with_nothing_sanitizable_falls_back_to_a_hash_only_name()
	{
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(new List<string> { "!!!" }));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		var bindings = await migration.BuildAsync([]);
		var state = bindings.Single(b => b.LocalResourceId == "entity/!!!/state");

		Assert.That(state.Name, Is.EqualTo("ha_e2d53a722"));
	}

	[Test]
	public async Task Bindings_already_present_for_a_resource_are_not_reproduced()
	{
		// Simulates a crash between saving the first attempt's bindings and calling MarkCompletedAsync: the
		// next BuildAsync call must skip everything already bound rather than minting a "-2" twin with a
		// new Guid.
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(_singleWatchedEntity));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		var firstAttempt = await migration.BuildAsync([]);
		var secondAttempt = await migration.BuildAsync(firstAttempt);

		Assert.That(secondAttempt, Is.Empty);
	}

	[Test]
	public async Task The_watched_entity_migration_runs_only_once()
	{
		var config = new FakeIntegrationConfig(JsonSerializer.Serialize(_singleWatchedEntity));
		var migration = new HomeAssistantWatchedEntityMigration(config);

		Assert.That(await migration.HasRunAsync(), Is.False);

		await migration.MarkCompletedAsync();

		Assert.That(await migration.HasRunAsync(), Is.True);
		Assert.That(await migration.BuildAsync([]),
			Is.Empty,
			"the watched-entities value was cleared once the migration completed");
	}

	[Test]
	public async Task Discovery_pages_and_searches_without_dropping_entities()
	{
		var catalog = CatalogOf(State("light.a", "on", ("friendly_name", "Entity A")),
			State("light.b", "on", ("friendly_name", "Entity B")),
			State("light.c", "on", ("friendly_name", "Special Room")),
			State("light.d", "on", ("friendly_name", "Entity D")),
			State("light.e", "on", ("friendly_name", "Entity E")));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var seen = new List<string>();
		string? token = null;
		do
		{
			var page = await provider.DiscoverAsync(
				new VariableCatalogQuery { ContinuationToken = token, PageSize = 2 });
			seen.AddRange(page.Items.Select(item => item.Id!));
			token = page.ContinuationToken;
		} while (token is not null);

		Assert.That(seen,
			Is.EquivalentTo([
				"entity/light.a", "entity/light.b", "entity/light.c", "entity/light.d", "entity/light.e"
			]));

		var byEntityId = await provider.DiscoverAsync(new VariableCatalogQuery { Search = "light.c" });
		var byFriendlyName = await provider.DiscoverAsync(new VariableCatalogQuery { Search = "Special" });

		Assert.Multiple(() =>
		{
			Assert.That(byEntityId.Items.Select(item => item.Id!), Is.EquivalentTo(["entity/light.c"]));
			Assert.That(byFriendlyName.Items.Select(item => item.Id!), Is.EquivalentTo(["entity/light.c"]));
		});
	}

	[Test]
	public async Task An_unknown_entity_does_not_resolve_but_a_known_unreachable_one_does()
	{
		// The catalog here stands in for what HomeAssistantConnection keeps: it is not cleared when the
		// upstream connection drops, only replaced on the next successful reconnect (see
		// HomeAssistantConnectionTests.A_dropped_connection_keeps_the_catalogue_but_clears_the_live_state).
		// So an entity that is merely unreachable right now still shows up here and must still resolve.
		var catalog = CatalogOf(State("light.known", "on"));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var known = await provider.ResolveAsync("entity/light.known/state");
		var unknown = await provider.ResolveAsync("entity/light.never_seen/state");

		Assert.Multiple(() =>
		{
			Assert.That(known, Is.Not.Null);
			Assert.That(unknown, Is.Null);
		});
	}

	[Test]
	public async Task A_known_entity_but_never_exposed_attribute_does_not_resolve()
	{
		var catalog = CatalogOf(State("light.living_room", "on", ("brightness", 128)));
		var provider = new HomeAssistantVariableCatalog(() => catalog);

		var result = await provider.ResolveAsync("entity/light.living_room/color_temp");

		Assert.That(result, Is.Null);
	}

	private static HomeAssistantEntityState State(
		string entityId,
		string state,
		params (string Name, object? Value)[] attributes)
	{
		var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var (name, value) in attributes)
		{
			dictionary[name] = value;
		}

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(dictionary));
		return new HomeAssistantEntityState(entityId, state, document.RootElement.Clone());
	}

	private static HomeAssistantCatalog CatalogOf(params HomeAssistantEntityState[] states)
	{
		var entities = new Dictionary<string, HomeAssistantEntityState>(StringComparer.Ordinal);
		foreach (var state in states)
		{
			entities[state.EntityId] = state;
		}

		return new HomeAssistantCatalog { Entities = entities };
	}

	private sealed class FakeIntegrationConfig : IIntegrationConfig
	{
		private readonly Guid _entryId = Guid.NewGuid();
		private readonly Dictionary<string, string?> _values;

		public FakeIntegrationConfig(string watchedEntitiesJson)
		{
			_values = new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.WatchedEntities] = watchedEntitiesJson
			};
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(
				[new ConfigEntrySnapshot(_entryId, "Home Assistant")]);

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
}
