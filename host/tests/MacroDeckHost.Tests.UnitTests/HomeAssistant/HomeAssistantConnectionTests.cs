using System.Collections.Concurrent;
using System.Diagnostics;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantConnectionTests
{
	private static readonly Uri _uri = new("ws://homeassistant.local:8123/api/websocket");

	private static readonly string[] _connectedEventDisconnected =
	[
		HomeAssistantEventIds.Connected, HomeAssistantEventIds.Event, HomeAssistantEventIds.Disconnected
	];

	private ClientFactory _factory = null!;
	private RecordingPublisher _publisher = null!;
	private HomeAssistantEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_factory = new ClientFactory();
		_publisher = new RecordingPublisher();
		_emitter = new HomeAssistantEventEmitter(_publisher);
	}

	[Test]
	public async Task Connecting_seeds_the_state_the_catalogue_and_the_entity_cache()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.Version, Is.EqualTo("2026.8.0"));
			Assert.That(connection.State.LocationName, Is.EqualTo("Home"));
			Assert.That(connection.State.EntityCount, Is.EqualTo(2));
			Assert.That(connection.Catalog.Entities.Keys,
				Is.EquivalentTo(new List<string> { "light.kitchen", "sensor.temp" }));
			Assert.That(connection.Catalog.Domains, Is.EqualTo(new List<string> { "light", "sensor" }));
			Assert.That(connection.Entity("light.kitchen")?.State, Is.EqualTo("on"));
		});
	}

	[Test]
	public async Task Exactly_one_untyped_event_subscription_is_made()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(_factory.Last.RequestNames.Count(name => name == "subscribe_events"), Is.EqualTo(1));
	}

	[Test]
	public async Task A_state_changed_event_updates_the_cache_and_publishes_entity_state_changed()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.RaiseEvent("state_changed",
			"""
			{ "entity_id": "light.kitchen",
			  "old_state": { "entity_id": "light.kitchen", "state": "on" },
			  "new_state": { "entity_id": "light.kitchen", "state": "off", "attributes": {} } }
			""");

		await WaitForAsync(() => connection.Entity("light.kitchen")?.State == "off", "the cache to update");
		Assert.That(_publisher.Ids, Has.Member(HomeAssistantEventIds.EntityStateChanged));
	}

	[Test]
	public async Task A_generic_event_publishes_the_event_trigger()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.RaiseEvent("automation_triggered", """{ "entity_id": "automation.morning" }""");

		await WaitForAsync(() => _publisher.Ids.Contains(HomeAssistantEventIds.Event), "the event to publish");
	}

	[Test]
	public async Task Connected_and_disconnected_are_published_once_per_session()
	{
		using var connection = Create(reconnectDelayMs: 30_000);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.RaiseEvent("automation_triggered", """{}""");
		_factory.Last.Drop();
		await WaitForAsync(() => !connection.IsConnected, "the disconnect");

		Assert.That(_publisher.Ids.Take(3), Is.EqualTo(_connectedEventDisconnected));
	}

	[Test]
	public async Task A_dropped_connection_keeps_the_catalogue_but_clears_the_live_state()
	{
		// A long retry delay so the reconnect cannot race the assertions back into "connected".
		using var connection = Create(reconnectDelayMs: 30_000);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.Drop();
		await WaitForAsync(() => !connection.IsConnected, "the state to be cleared");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.Version, Is.Null);
			Assert.That(connection.State.EntityCount, Is.Null);
			Assert.That(connection.Catalog.Entities, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_failed_catalogue_refresh_never_downgrades_a_known_catalogue_to_an_empty_one()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.FailingRequests.Add("get_services");
		_factory.Last.RaiseEvent("entity_registry_updated");

		await WaitForAsync(() => _factory.Last.RequestNames.Count(name => name == "get_services") >= 2,
			"the refresh attempt");

		Assert.That(connection.Catalog.Services["light"], Is.EqualTo(new List<string> { "turn_off", "turn_on" }));
	}

	[Test]
	public async Task A_burst_of_registry_events_collapses_into_one_catalogue_refresh()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		for (var i = 0; i < 5; i++)
		{
			_factory.Last.RaiseEvent("entity_registry_updated");
		}

		await WaitForAsync(() => _factory.Last.RequestNames.Count(name => name == "get_services") >= 2,
			"the debounced catalogue refresh");
		await Task.Delay(TimeSpan.FromMilliseconds(750));
		Assert.That(_factory.Last.RequestNames.Count(name => name == "get_services"), Is.EqualTo(2));
	}

	[Test]
	public async Task An_auth_invalid_response_stops_the_pump_and_sets_the_flag()
	{
		_factory.Script.Enqueue(client =>
			client.ConnectException = new HomeAssistantAuthenticationException("bad token"));

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.AuthInvalid, "the authentication failure to be reported");

		// Retrying a rejected token cannot help, so no second attempt is made.
		await Task.Delay(150);
		Assert.That(_factory.Created, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_certificate_failure_sets_the_flag_and_clears_on_the_next_success()
	{
		_factory.Script.Enqueue(client
			=> client.ConnectException = new HomeAssistantTlsException("untrusted", new InvalidOperationException()));

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.CertificateUntrusted, "the certificate failure to be reported");

		await WaitForAsync(() => connection.IsConnected, "the retry to succeed");
		Assert.That(connection.CertificateUntrusted, Is.False);
	}

	[Test]
	public async Task Unreachable_appears_only_after_five_consecutive_failures_and_clears_on_success()
	{
		for (var i = 0; i < 5; i++)
		{
			_factory.Script.Enqueue(client => client.ConnectException = new InvalidOperationException("refused"));
		}

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.Unreachable, "the fifth failure to be reported");
		Assert.That(_factory.Created, Has.Count.EqualTo(5));

		await WaitForAsync(() => connection.IsConnected, "the sixth attempt to succeed");
		Assert.That(connection.Unreachable, Is.False);
	}

	[Test]
	public async Task Unreachable_does_not_appear_before_the_fifth_failure()
	{
		for (var i = 0; i < 3; i++)
		{
			_factory.Script.Enqueue(client => client.ConnectException = new InvalidOperationException("refused"));
		}

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => _factory.Created.Count >= 3, "three failed attempts");
		await Task.Delay(50);

		Assert.That(connection.Unreachable, Is.False);
	}

	[Test]
	public async Task The_reconnect_delay_increases_across_consecutive_failures()
	{
		for (var i = 0; i < 3; i++)
		{
			_factory.Script.Enqueue(client => client.FailingRequests.Add("get_states"));
		}

		using var connection = Create(reconnectDelayMs: 60);
		var timestamps = new List<long>();

		connection.Start();
		await WaitForAsync(() => _factory.Created.Count >= 3, "three failed connection attempts", timeout: 10);

		foreach (var client in _factory.Created)
		{
			timestamps.Add(client.CreatedAtMs);
		}

		var firstGap = timestamps[1] - timestamps[0];
		var secondGap = timestamps[2] - timestamps[1];

		Assert.That(secondGap, Is.GreaterThan(firstGap * 1.3));
	}

	[Test]
	public async Task A_socket_that_connects_and_drops_immediately_does_not_tighten_the_reconnect_loop()
	{
		for (var i = 0; i < 4; i++)
		{
			_factory.Script.Enqueue(client => client.FailingRequests.Add("get_states"));
		}

		using var connection = Create(reconnectDelayMs: 40);
		connection.Start();

		await WaitForAsync(() => _factory.Created.Count >= 4, "four failed sessions", timeout: 10);

		Assert.That(connection.IsConnected, Is.False);
	}

	[Test]
	public async Task Backoff_resets_only_after_get_states_succeeds()
	{
		_factory.Script.Enqueue(client => client.FailingRequests.Add("get_states"));

		const int baseDelayMs = 150;
		using var connection = Create(reconnectDelayMs: baseDelayMs);
		connection.Start();

		await WaitForAsync(() => _factory.Created.Count >= 2, "the retry after the first failure");

		await WaitForAsync(() => connection.IsConnected, "the successful session");
		var beforeDrop = Stopwatch.StartNew();
		_factory.Last.Drop();

		await WaitForAsync(() => _factory.Created.Count >= 3, "the reconnect after the reset");
		var resetDelayMs = beforeDrop.ElapsedMilliseconds;

		Assert.That(resetDelayMs, Is.LessThan(baseDelayMs * 2));
	}

	[Test]
	public async Task Disposing_stops_the_pump()
	{
		var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		connection.Dispose();
		var attempts = _factory.Created.Count;
		_factory.Last.Drop();
		await Task.Delay(150);

		Assert.That(_factory.Created, Has.Count.EqualTo(attempts));
	}

	[Test]
	public async Task CallServiceAsync_fails_gracefully_while_disconnected()
	{
		using var connection = Create();

		var error = await connection.CallServiceAsync("light", "turn_on", null, null);

		Assert.That(TestLocalization.Resolve(error), Does.Contain("not connected"));
	}

	[Test]
	public async Task CallServiceAsync_sends_the_domain_service_target_and_data()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var error = await connection.CallServiceAsync("light",
			"turn_on",
			new Dictionary<string, object?> { ["entity_id"] = "light.kitchen" },
			new Dictionary<string, object?> { ["brightness_pct"] = 50 });

		Assert.That(error, Is.Null);

		var payload = _factory.Last.PayloadOf("call_service");
		Assert.Multiple(() =>
		{
			Assert.That(payload?["domain"], Is.EqualTo("light"));
			Assert.That(payload?["service"], Is.EqualTo("turn_on"));
		});
	}

	private HomeAssistantConnection Create(int reconnectDelayMs = 20)
		=> new(_factory.Create,
			_uri,
			"the-token",
			events: _emitter,
			reconnectDelay: TimeSpan.FromMilliseconds(reconnectDelayMs));

	private static async Task WaitForAsync(Func<bool> condition, string because, int timeout = 5)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(timeout))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private sealed class ClientFactory
	{
		private static readonly Stopwatch _clock = Stopwatch.StartNew();

		public List<FakeHomeAssistantClient> Created { get; } = [];

		public Queue<Action<FakeHomeAssistantClient>> Script { get; } = new();

		public FakeHomeAssistantClient Last => Created[^1];

		public FakeHomeAssistantClient Create()
		{
			var client = new FakeHomeAssistantClient
			{
				Responses =
				{
					["get_states"] =
						"""
						[ { "entity_id": "light.kitchen", "state": "on", "attributes": { "friendly_name": "Kitchen" } },
						  { "entity_id": "sensor.temp", "state": "21.5", "attributes": {} } ]
						""",
					["get_config"] = """{ "location_name": "Home", "version": "2026.8.0" }""",
					["get_services"] = """{ "light": { "turn_on": {}, "turn_off": {} } }""",
					["config/area_registry/list"] = "[]",
					["config/device_registry/list"] = "[]",
					["config/entity_registry/list"] = "[]"
				}
			};

			if (Script.Count > 0)
			{
				Script.Dequeue().Invoke(client);
			}

			client.CreatedAtMs = _clock.ElapsedMilliseconds;
			Created.Add(client);
			return client;
		}
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		private readonly ConcurrentQueue<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> _published
			= new();

		public IReadOnlyList<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published
			=> _published.ToList();

		public IEnumerable<string> Ids => Published.Select(entry => entry.EventId);

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> _published.Enqueue((eventId, parameters));
	}
}
