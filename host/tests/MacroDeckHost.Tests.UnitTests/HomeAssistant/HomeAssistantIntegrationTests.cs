using System.Diagnostics;
using System.Reflection;
using System.Text;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantIntegrationTests
{
	private static readonly Uri _uri = new("ws://homeassistant.local:8123/api/websocket");

	private static readonly string[] _migratedDeskSwitchVariableNames = ["ha_switch_desk", "ha_switch_desk_attributes"];

	private HomeAssistantIntegration _integration = null!;

	[SetUp]
	public void SetUp() => _integration = new HomeAssistantIntegration();

	[TearDown]
	public void TearDown() => _integration.Dispose();

	[Test]
	public void The_integration_is_identified()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.homeassistant"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("Home Assistant"));
			Assert.That(
				typeof(HomeAssistantIntegration).GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false),
				Is.Not.Empty);
		});
	}

	[Test]
	public void The_host_discovery_finds_the_integration_and_can_construct_it()
	{
		var discovered = IntegrationDiscovery.DiscoverIntegrations(Serilog.Log.Logger);

		var homeAssistant = discovered.OfType<HomeAssistantIntegration>().SingleOrDefault();
		Assert.That(homeAssistant, Is.Not.Null, "the Home Assistant integration was not discovered");
		Assert.Multiple(() =>
		{
			Assert.That(homeAssistant!.Actions, Is.Not.Empty);
			Assert.That(homeAssistant.Variables, Has.Count.EqualTo(4));
			Assert.That(homeAssistant.EventDefinitions.Select(definition => definition.Id),
				Is.EquivalentTo(new List<string>
				{
					HomeAssistantEventIds.EntityStateChanged,
					HomeAssistantEventIds.Event,
					HomeAssistantEventIds.Connected,
					HomeAssistantEventIds.Disconnected
				}));
		});

		homeAssistant!.Dispose();
	}

	[Test]
	public void Setup_configures_a_single_instance()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.AllowsMultipleConfigurations, Is.False);
			Assert.That(_integration.CreateConfigFlow(), Is.InstanceOf<HomeAssistantConfigFlow>());
		});
	}

	[Test]
	public async Task Everything_but_the_connection_flag_is_unknown_while_disconnected()
	{
		// A restarting Home Assistant must not read as a home with no entities in it.
		Assert.That((await _integration.ReadAsync("homeassistant-is-connected", CancellationToken.None)).Value,
			Is.EqualTo(false));

		foreach (var variable in _integration.Variables.Where(v => v.Name != "homeassistant_is_connected"))
		{
			Assert.That((await _integration.ReadAsync(variable.ResolvedId!, CancellationToken.None)).Value,
				Is.Null,
				variable.Name);
		}
	}

	[Test]
	public async Task An_unknown_variable_answers_null()
	{
		Assert.That((await _integration.ReadAsync("nonsense", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public void The_entity_state_changed_filters_share_their_names_with_its_payload()
	{
		var definition = _integration.EventDefinitions.Single(e => e.Id == HomeAssistantEventIds.EntityStateChanged);
		var payloadNames = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();

		Assert.Multiple(() =>
		{
			foreach (var configuration in definition.ConfigurationParameters)
			{
				Assert.That(payloadNames, Has.Member(configuration.Name));
				Assert.That(configuration.Required, Is.False, "an unset filter has to mean 'any'");
			}
		});
	}

	[Test]
	public void Every_event_parameter_uses_a_defined_parameter_type()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in _integration.EventDefinitions)
			{
				foreach (var parameter in definition.ConfigurationParameters.Concat(definition.PayloadParameters))
				{
					Assert.That(Enum.IsDefined(parameter.Type), Is.True, $"{definition.Id}.{parameter.Name}");
				}
			}
		});
	}

	[Test]
	public async Task Event_options_answer_instantly_while_disconnected_and_stay_typeable()
	{
		var entities = await _integration.GetEventOptionsAsync(
			Options(HomeAssistantEventIds.EntityStateChanged, "entityId"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(entities.Options, Is.Empty);
			Assert.That(entities.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task An_unknown_event_parameter_has_no_options()
	{
		var result = await _integration.GetEventOptionsAsync(Options("nonsense", "nonsense"), CancellationToken.None);

		Assert.That(result.Options, Is.Empty);
	}

	[Test]
	public async Task Event_options_fill_from_the_connections_cached_catalogue_once_connected()
	{
		await using var connected = await ConnectedAsync();
		InjectConnection(connected.Connection);

		var entities = await _integration.GetEventOptionsAsync(
			Options(HomeAssistantEventIds.EntityStateChanged, "entityId"),
			CancellationToken.None);
		var domains = await _integration.GetEventOptionsAsync(
			Options(HomeAssistantEventIds.EntityStateChanged, "domain"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(entities.Options.Select(o => o.Value), Has.Member("light.kitchen"));
			Assert.That(domains.Options.Select(o => o.Value), Has.Member("light"));
		});
	}

	[Test]
	public async Task No_issue_is_reported_just_because_home_assistant_is_closed()
	{
		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues, Is.Empty);
	}

	[Test]
	public async Task An_unknown_issue_cannot_be_resolved()
	{
		var resolution = await _integration.ResolveIssueAsync("nonsense");

		Assert.That(resolution.Success, Is.False);
	}

	[TestCase(HomeAssistantIntegration.AuthInvalidIssueId, IntegrationIssueSeverity.Error)]
	[TestCase(HomeAssistantIntegration.CertificateIssueId, IntegrationIssueSeverity.Error)]
	[TestCase(HomeAssistantIntegration.UnreachableIssueId, IntegrationIssueSeverity.Warning)]
	public async Task An_auth_invalid_session_reports_the_matching_issue(
		string issueId,
		IntegrationIssueSeverity expectedSeverity)
	{
		HomeAssistantConnection connection;
		if (issueId == HomeAssistantIntegration.AuthInvalidIssueId)
		{
			connection = await FailingAsync(client
					=> client.ConnectException = new HomeAssistantAuthenticationException("bad token"),
				condition: c => c.AuthInvalid);
		}
		else if (issueId == HomeAssistantIntegration.CertificateIssueId)
		{
			connection = await FailingAsync(client
					=> client.ConnectException
						= new HomeAssistantTlsException("untrusted", new InvalidOperationException()),
				condition: c => c.CertificateUntrusted);
		}
		else
		{
			connection = await UnreachableAsync();
		}

		InjectConnection(connection);
		var issues = await _integration.GetIssuesAsync();
		var issue = issues.Single(i => i.Id == issueId);

		var resolution = await _integration.ResolveIssueAsync(issueId);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(expectedSeverity));
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
		});

		connection.Dispose();
	}

	[Test]
	public void The_brand_icon_is_a_non_empty_svg()
	{
		var icon = Encoding.UTF8.GetString(_integration.GetIcon());

		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(icon, Does.Contain("<svg"));
			Assert.That(icon.Length, Is.GreaterThan(0));
		});
	}

	[Test]
	public void Shutdown_is_safe_twice_and_without_ever_having_connected()
	{
		Assert.DoesNotThrowAsync(() => _integration.ShutdownAsync());
		Assert.DoesNotThrowAsync(() => _integration.ShutdownAsync());
		Assert.DoesNotThrow(() => _integration.Dispose());
	}

	[Test]
	public async Task Initializing_migrates_previously_watched_entities_into_the_binding_store()
	{
		var context = new FakeHomeAssistantIntegrationContext();
		var entryId = context.ConfigStore.AddEntry("Home Assistant",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.WatchedEntities] = "[\"switch.desk\"]"
			});
		var store = new FakeVariableBindingStore();
		((IHomeAssistantBindingStoreConsumer)_integration).UseBindingStore(store);

		await _integration.InitializeAsync(context);

		Assert.Multiple(() =>
		{
			Assert.That(store.Load().Select(b => b.Name), Is.EquivalentTo(_migratedDeskSwitchVariableNames));
			Assert.That(store.Load(),
				Has.All.Matches<Domain.Entities.VariableBinding>(b =>
					b.MigratedFrom == HomeAssistantWatchedEntityMigration.MigratedFromId));
		});

		var migrated
			= await context.ConfigStore.GetStringAsync(entryId, HomeAssistantConfigKeys.WatchedEntitiesMigrated);
		var watchedAfterMigration
			= await context.ConfigStore.GetStringAsync(entryId, HomeAssistantConfigKeys.WatchedEntities);

		Assert.Multiple(() =>
		{
			Assert.That(migrated, Is.EqualTo("true"));
			Assert.That(watchedAfterMigration, Is.Null, "the compatibility key is cleared once migrated");
		});
	}

	[Test]
	public async Task Initializing_a_second_time_does_not_re_run_or_duplicate_the_migration()
	{
		var context = new FakeHomeAssistantIntegrationContext();
		context.ConfigStore.AddEntry("Home Assistant",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.WatchedEntities] = "[\"switch.desk\"]"
			});
		var store = new FakeVariableBindingStore();
		((IHomeAssistantBindingStoreConsumer)_integration).UseBindingStore(store);

		await _integration.InitializeAsync(context);
		await _integration.InitializeAsync(context);

		Assert.That(store.Load(), Has.Count.EqualTo(2), "the second run must not mint duplicate bindings");
	}

	[Test]
	public async Task No_binding_store_means_no_migration_but_initialization_still_succeeds()
	{
		var context = new FakeHomeAssistantIntegrationContext();
		context.ConfigStore.AddEntry("Home Assistant",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.WatchedEntities] = "[\"switch.desk\"]"
			});

		Assert.DoesNotThrowAsync(() => _integration.InitializeAsync(context));
	}

	[Test]
	public async Task A_failed_save_does_not_mark_the_migration_complete()
	{
		var context = new FakeHomeAssistantIntegrationContext();
		context.ConfigStore.AddEntry("Home Assistant",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[HomeAssistantConfigKeys.WatchedEntities] = "[\"switch.desk\"]"
			});
		var store = new FakeVariableBindingStore { FailNextSave = true };
		((IHomeAssistantBindingStoreConsumer)_integration).UseBindingStore(store);

		await _integration.InitializeAsync(context);

		Assert.That(store.Load(), Is.Empty, "the failed save must not be treated as durable");

		// The next start (a fresh integration instance, same store and config) must retry rather than
		// having given up because MarkCompletedAsync never ran.
		var retryIntegration = new HomeAssistantIntegration();
		((IHomeAssistantBindingStoreConsumer)retryIntegration).UseBindingStore(store);
		await retryIntegration.InitializeAsync(context);
		retryIntegration.Dispose();

		Assert.That(store.Load().Select(b => b.Name), Is.EquivalentTo(_migratedDeskSwitchVariableNames));
	}

	private static EventOptionsContext Options(string eventId, string parameterName)
		=> new()
		{
			EventId = eventId,
			ParameterName = parameterName,
			CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
		};

	private void InjectConnection(HomeAssistantConnection connection)
	{
		var field = typeof(HomeAssistantIntegration).GetField("_connection",
			BindingFlags.NonPublic | BindingFlags.Instance);
		field!.SetValue(_integration, connection);
	}

	private static async Task<ConnectedConnection> ConnectedAsync()
	{
		var client = new FakeHomeAssistantClient
		{
			Responses =
			{
				["get_states"] =
					"""[ { "entity_id": "light.kitchen", "state": "on", "attributes": { "friendly_name": "Kitchen" } } ]""",
				["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }""",
				["get_services"] = """{ "light": { "turn_on": {} } }""",
				["config/area_registry/list"] = "[]",
				["config/device_registry/list"] = "[]",
				["config/entity_registry/list"] = "[]"
			}
		};

		var connection = new HomeAssistantConnection(() => client, _uri, "the-token");
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		return new ConnectedConnection(connection);
	}

	private static async Task<HomeAssistantConnection> FailingAsync(
		Action<FakeHomeAssistantClient> configure,
		Func<HomeAssistantConnection, bool> condition)
	{
		var client = new FakeHomeAssistantClient();
		configure(client);

		var connection = new HomeAssistantConnection(() => client, _uri, "the-token");
		connection.Start();
		await WaitForAsync(() => condition(connection), "the failure to be reported");
		return connection;
	}

	private static async Task<HomeAssistantConnection> UnreachableAsync()
	{
		var attempt = 0;
		var connection
			= new HomeAssistantConnection(Factory, _uri, "the-token", reconnectDelay: TimeSpan.FromMilliseconds(20));
		connection.Start();
		await WaitForAsync(() => connection.Unreachable, "the fifth failure to be reported");
		return connection;

		IHomeAssistantClient Factory()
		{
			attempt++;
			var client = new FakeHomeAssistantClient();
			if (attempt <= 5)
			{
				client.ConnectException = new InvalidOperationException("refused");
			}

			return client;
		}
	}

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private sealed class ConnectedConnection : IAsyncDisposable
	{
		public ConnectedConnection(HomeAssistantConnection connection)
		{
			Connection = connection;
		}

		public HomeAssistantConnection Connection { get; }

		public ValueTask DisposeAsync()
		{
			Connection.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
