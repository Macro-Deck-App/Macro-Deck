using System.Diagnostics;
using MacroDeckHost.Integrations.Streamerbot;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotConnectionTests
{
	private const string ActionId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

	private static readonly Uri _uri = new("ws://127.0.0.1:8080/");

	private static readonly StreamerbotInstanceInfo _info = new("Streamer.bot", "0.2.5", "instance-1", "windows");

	private static readonly string[] _actionNames = ["Shoutout", "Timer"];
	private static readonly string[] _codeTriggerNames = ["My Trigger"];
	private static readonly string[] _twitchEvents = ["Follow", "Sub"];
	private static readonly string[] _eventSources = ["Twitch", "General"];

	private static readonly string[] _connectedEventDisconnected =
	[
		StreamerbotEventIds.Connected, StreamerbotEventIds.Event, StreamerbotEventIds.Disconnected
	];

	private ClientFactory _factory = null!;
	private RecordingPublisher _publisher = null!;
	private StreamerbotEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_factory = new ClientFactory();
		_publisher = new RecordingPublisher();
		_emitter = new StreamerbotEventEmitter(_publisher);
	}

	[Test]
	public async Task Connecting_seeds_the_state_from_the_greeting_and_the_broadcaster()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.Version, Is.EqualTo("0.2.5"));
			Assert.That(connection.State.InstanceName, Is.EqualTo("Streamer.bot"));
			Assert.That(connection.State.BroadcasterName, Is.EqualTo("Ada"));
			Assert.That(connection.State.BroadcasterPlatform, Is.EqualTo("twitch"));
		});
	}

	[Test]
	public async Task Connecting_caches_the_actions_triggers_and_events()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.Multiple(() =>
		{
			Assert.That(connection.Catalog.Actions.Select(action => action.Name), Is.EqualTo(_actionNames));
			Assert.That(connection.Catalog.CodeTriggers.Select(trigger => trigger.Name), Is.EqualTo(_codeTriggerNames));
			Assert.That(connection.Catalog.Events["Twitch"], Is.EqualTo(_twitchEvents));
		});
	}

	[Test]
	public async Task Connecting_subscribes_to_every_event_the_instance_knows()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var payload = _factory.Last.PayloadOf("Subscribe");
		Assert.That(payload, Is.Not.Null);

		var events = payload!["events"] as IReadOnlyDictionary<string, IReadOnlyList<string>>;
		Assert.That(events, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(events!.Keys, Is.EquivalentTo(_eventSources));
			Assert.That(events["Twitch"], Is.EqualTo(_twitchEvents));
		});
	}

	[Test]
	public async Task A_server_that_challenges_is_answered_with_the_hashed_password()
	{
		_factory.Configure = client => client.Hello = new StreamerbotHello(_info,
			new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));

		using var connection = Create(password: "streamer");
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var payload = _factory.Last.PayloadOf("Authenticate");
		Assert.That(payload?["authentication"], Is.EqualTo("AFIXJSAwz+e4zaC0apEHiiQ3YXU6wgdXV7CGJXdUF1s="));
	}

	[Test]
	public async Task A_server_without_a_challenge_is_never_sent_a_password()
	{
		using var connection = Create(password: "streamer");
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(_factory.Last.RequestNames, Has.No.Member("Authenticate"));
	}

	[Test]
	public async Task A_rejected_password_stops_the_loop_and_raises_an_issue()
	{
		_factory.Configure = client =>
		{
			client.Hello = new StreamerbotHello(_info, new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));
			client.FailingRequests.Add("Authenticate");
		};

		using var connection = Create(password: "wrong");
		connection.Start();

		await WaitForAsync(() => connection.NeedsAuthentication, "the authentication failure to be reported");

		// Retrying cannot fix a wrong password, so no second attempt is made.
		await Task.Delay(150);
		Assert.Multiple(() =>
		{
			Assert.That(_factory.Created, Has.Count.EqualTo(1));
			Assert.That(connection.IsConnected, Is.False);
		});
	}

	[Test]
	public async Task A_server_that_enforces_authentication_without_a_password_stops_the_loop()
	{
		_factory.Configure = client =>
		{
			client.Hello = new StreamerbotHello(_info, new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));
			client.FailingRequests.Add("GetInfo");
		};

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.NeedsAuthentication, "the authentication failure to be reported");
		Assert.That(_factory.Created, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_server_that_challenges_but_does_not_enforce_connects_without_a_password()
	{
		_factory.Configure = client => client.Hello = new StreamerbotHello(_info,
			new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		Assert.That(connection.NeedsAuthentication, Is.False);
	}

	[Test]
	public async Task A_server_that_does_not_greet_is_identified_with_GetInfo()
	{
		_factory.Configure = client => client.Hello = null;

		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.Multiple(() =>
		{
			Assert.That(_factory.Last.RequestNames, Has.Member("GetInfo"));
			Assert.That(connection.State.Version, Is.EqualTo("0.2.5"));
		});
	}

	[Test]
	public async Task A_dropped_connection_is_retried()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		_factory.Last.Drop();

		await WaitForAsync(() => _factory.Created.Count >= 2, "a reconnect attempt");
	}

	[Test]
	public async Task A_dropped_connection_clears_the_state_but_keeps_the_catalogue()
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
			Assert.That(connection.State.BroadcasterName, Is.Null);
			Assert.That(connection.Catalog.Actions, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_failed_catalogue_reload_keeps_the_last_known_actions()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.FailingRequests.Add("GetActions");
		_factory.Last.RaiseEvent("Application", "ActionUpdated");

		await WaitForAsync(() => _factory.Last.RequestNames.Count(name => name == "GetActions") >= 2,
			"the catalogue refresh");

		Assert.That(connection.Catalog.Actions, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task An_action_edit_refreshes_the_catalogue()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.Responses["GetActions"] =
			"""{ "status": "ok", "count": 1, "actions": [ { "id": "a", "name": "Renamed", "group": "", "enabled": true } ] }""";
		_factory.Last.RaiseEvent("Application", "ActionUpdated");

		await WaitForAsync(() => connection.Catalog.Actions.Count == 1, "the refreshed catalogue");
		Assert.That(connection.Catalog.Actions[0].Name, Is.EqualTo("Renamed"));
	}

	[Test]
	public async Task An_empty_action_list_is_a_real_answer_and_replaces_the_cache()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.Responses["GetActions"] = """{ "status": "ok", "count": 0, "actions": [] }""";
		_factory.Last.RaiseEvent("Application", "ActionDeleted");

		await WaitForAsync(() => connection.Catalog.Actions.Count == 0, "the emptied catalogue");
	}

	[Test]
	public async Task Events_are_published_with_the_connection_lifecycle_around_them()
	{
		using var connection = Create(reconnectDelayMs: 30_000);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.RaiseEvent("Twitch", "Follow", """{ "user_name": "Ada" }""");
		_factory.Last.Drop();
		await WaitForAsync(() => !connection.IsConnected, "the disconnect");

		Assert.That(_publisher.Ids.Take(3), Is.EqualTo(_connectedEventDisconnected));
	}

	[Test]
	public async Task An_action_is_run_by_id_when_the_value_is_a_guid()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await connection.DoActionAsync(ActionId, null);

		var identity = _factory.Last.PayloadOf("DoAction")?["action"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(identity?["id"], Is.EqualTo(ActionId));
			Assert.That(identity?.ContainsKey("name"), Is.False);
		});
	}

	[Test]
	public async Task An_action_is_run_by_name_when_the_value_is_not_a_guid()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await connection.DoActionAsync("Shoutout", new Dictionary<string, object?> { ["target"] = "ada" });

		var payload = _factory.Last.PayloadOf("DoAction");
		var identity = payload?["action"] as IReadOnlyDictionary<string, object?>;
		Assert.Multiple(() =>
		{
			Assert.That(identity?["name"], Is.EqualTo("Shoutout"));
			Assert.That(identity?.ContainsKey("id"), Is.False);
			Assert.That((payload?["args"] as IReadOnlyDictionary<string, object?>)?["target"], Is.EqualTo("ada"));
		});
	}

	[Test]
	public async Task A_chat_message_is_refused_when_the_session_did_not_authenticate()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await connection.SendChatMessageAsync("twitch", "hi", asBot: false);

		Assert.That(_factory.Last.RequestNames, Has.No.Member("SendMessage"));
	}

	[Test]
	public async Task A_chat_message_is_sent_once_the_session_authenticated()
	{
		_factory.Configure = client => client.Hello = new StreamerbotHello(_info,
			new StreamerbotChallenge("c2FsdA==", "Y2hhbGxlbmdl"));

		using var connection = Create(password: "streamer");
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await connection.SendChatMessageAsync("twitch", "hi", asBot: true);

		var payload = _factory.Last.PayloadOf("SendMessage");
		Assert.Multiple(() =>
		{
			Assert.That(payload?["platform"], Is.EqualTo("twitch"));
			Assert.That(payload?["message"], Is.EqualTo("hi"));
			Assert.That(payload?["bot"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task A_global_variable_is_read_from_the_response()
	{
		_factory.Configure = client => client.Responses["GetGlobal"] =
			"""{ "status": "ok", "count": 1, "variables": { "raidCount": { "name": "raidCount", "value": 7 } } }""";

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var value = await connection.GetGlobalAsync("raidCount", persisted: true);

		Assert.That(value?.GetInt32(), Is.EqualTo(7));
	}

	[Test]
	public async Task A_global_variable_read_while_disconnected_answers_null()
	{
		using var connection = Create();

		var value = await connection.GetGlobalAsync("raidCount", persisted: true);

		Assert.That(value, Is.Null);
	}

	[Test]
	public async Task Requests_are_dropped_rather_than_thrown_while_disconnected()
	{
		using var connection = Create();

		Assert.DoesNotThrowAsync(() => connection.DoActionAsync("Shoutout", null));
		await Task.CompletedTask;
	}

	[Test]
	public async Task Disposing_stops_reconnecting()
	{
		var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		connection.Dispose();
		var attempts = _factory.Created.Count;
		_factory.Created[^1].Drop();
		await Task.Delay(150);

		Assert.That(_factory.Created, Has.Count.EqualTo(attempts));
	}

	[Test]
	public async Task Connecting_asks_for_an_eager_variable_refresh()
	{
		var requests = 0;
		using var connection = Create(onVariablesChanged: () => Interlocked.Increment(ref requests));
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		await WaitForAsync(() => Volatile.Read(ref requests) > 0, "an eager refresh request");

		Assert.That(Volatile.Read(ref requests), Is.GreaterThan(0));
	}

	private StreamerbotConnection Create(
		string? password = null,
		int reconnectDelayMs = 20,
		Action? onVariablesChanged = null)
		=> new(_factory.Create,
			_uri,
			password,
			_emitter,
			TimeSpan.FromMilliseconds(reconnectDelayMs),
			onVariablesChanged);

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private sealed class ClientFactory
	{
		public List<FakeStreamerbotClient> Created { get; } = [];

		public Action<FakeStreamerbotClient>? Configure { get; set; }

		public FakeStreamerbotClient Last => Created[^1];

		public FakeStreamerbotClient Create()
		{
			var client = new FakeStreamerbotClient
			{
				Hello = new StreamerbotHello(_info, null),
				Responses =
				{
					["GetInfo"] =
						"""{ "status": "ok", "info": { "instanceId": "instance-1", "name": "Streamer.bot", "version": "0.2.5", "os": "windows" } }""",
					["GetActions"] =
						"""
						{ "status": "ok", "count": 2, "actions": [
							{ "id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "name": "Shoutout", "group": "Chat", "enabled": true },
							{ "id": "3f2504e0-4f89-11d3-9a0c-0305e82c3302", "name": "Timer", "group": "", "enabled": false } ] }
						""",
					["GetCodeTriggers"] =
						"""{ "status": "ok", "count": 1, "triggers": [ { "name": "My Trigger", "eventName": "My Trigger", "category": "Custom" } ] }""",
					["GetEvents"] =
						"""{ "status": "ok", "events": { "Twitch": [ "Follow", "Sub" ], "General": [ "Custom" ] } }""",
					["GetBroadcaster"] =
						"""
						{ "status": "ok", "connected": [ "twitch" ], "disconnected": [],
						  "platforms": { "twitch": { "broadcastUser": "ada", "broadcastUserName": "Ada", "broadcastUserId": "1" } } }
						"""
				}
			};

			Configure?.Invoke(client);
			Created.Add(client);
			return client;
		}
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public IEnumerable<string> Ids => Published.Select(entry => entry.EventId);

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
