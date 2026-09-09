using System.Diagnostics;
using System.Text.Json;
using MacroDeckHost.Integrations.Meld;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldConnectionTests
{
	private const string OneTrackSessionJson =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private const string TwoTrackSessionJson =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"},
		"track2":{"name":"Track Two","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private const string LayerEffectSessionJson =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},
		"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},
		"effect1":{"parent":"layer1","name":"Effect","enabled":false,"type":"effect"}
		}}
		""";

	private static readonly Uri _uri = new("ws://127.0.0.1:13376/");
	private static readonly string[] _gainUpdatedOnly = ["meld.gainUpdated"];

	private ClientFactory _factory = null!;

	[SetUp]
	public void SetUp()
	{
		_factory = new ClientFactory();
	}

	[Test]
	public async Task Connecting_subscribes_to_gainUpdated_exactly_once()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(_factory.Last.ConnectedSignalSnapshot(), Is.EquivalentTo(_gainUpdatedOnly));
	}

	[Test]
	public async Task Connecting_parses_the_initial_session_and_streaming_state()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson,
				isStreaming: true,
				isRecording: false);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.Session.Scenes, Has.Count.EqualTo(1));
			Assert.That(connection.State.Session.Tracks, Has.Count.EqualTo(1));
			Assert.That(connection.State.IsStreaming, Is.True);
			Assert.That(connection.State.IsRecording, Is.False);
		});
	}

	[Test]
	public async Task ApiVersion_defaults_to_1_when_absent()
	{
		_factory.Configure = client =>
			client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo(version: null);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(connection.State.ApiVersion, Is.EqualTo(1));
	}

	[Test]
	public async Task ApiVersion_reads_the_reported_version()
	{
		_factory.Configure = client => client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo(version: 2);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(connection.State.ApiVersion, Is.EqualTo(2));
	}

	[Test]
	public async Task SupportsSetProperty_is_false_at_v2_without_the_setProperty_method()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(version: 2, supportsSetProperty: false);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.That(connection.State.SupportsSetProperty, Is.False);
	}

	[Test]
	public async Task Track_observers_are_registered_with_context_first_then_track_id()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Any(invocation => invocation.Method == MeldObjects.RegisterTrackObserver),
			"the observer registration");

		var args = _factory.Last.ArgsOf(MeldObjects.RegisterTrackObserver);
		Assert.Multiple(() =>
		{
			Assert.That(args, Is.Not.Null);
			Assert.That(args![0], Is.EqualTo(MeldObjects.IntegrationId));
			Assert.That(args[1], Is.EqualTo("track1"));
		});
	}

	[Test]
	public async Task One_observer_is_registered_per_track_on_connect()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: TwoTrackSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Count(invocation => invocation.Method == MeldObjects.RegisterTrackObserver) >=
				2,
			"both tracks to be observed");

		var registered = _factory.Last.InvocationSnapshot()
			.Where(invocation => invocation.Method == MeldObjects.RegisterTrackObserver)
			.Select(invocation => invocation.Args[1])
			.ToList();
		Assert.That(registered, Is.EquivalentTo(new object?[] { "track1", "track2" }));
	}

	[Test]
	public async Task A_session_change_that_adds_a_track_registers_only_the_new_one()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Any(invocation => invocation.Method == MeldObjects.RegisterTrackObserver),
			"the first observer");

		_factory.Last.RaisePropertyUpdate(MeldObjects.Object,
			new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[MeldObjects.SessionProperty] = FakeQWebChannelClient.Parse(TwoTrackSessionJson)
			});

		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Count(invocation => invocation.Method == MeldObjects.RegisterTrackObserver) >=
				2,
			"the second observer");

		var secondRegistration =
			_factory.Last.InvocationSnapshot()
				.Last(invocation => invocation.Method == MeldObjects.RegisterTrackObserver);
		Assert.That(secondRegistration.Args[1], Is.EqualTo("track2"));
	}

	[Test]
	public async Task Removing_a_track_unregisters_it_and_evicts_its_gain()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: TwoTrackSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Count(invocation => invocation.Method == MeldObjects.RegisterTrackObserver) >=
				2,
			"both observers");

		_factory.Last.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track2\""),
			FakeQWebChannelClient.Parse("0.5"),
			FakeQWebChannelClient.Parse("false"));
		await WaitForAsync(() => connection.TryGetGain("track2", out _), "the gain to be cached");

		_factory.Last.RaisePropertyUpdate(MeldObjects.Object,
			new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[MeldObjects.SessionProperty] = FakeQWebChannelClient.Parse(OneTrackSessionJson)
			});

		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Any(invocation => invocation.Method == MeldObjects.UnregisterTrackObserver),
			"the unregister call");

		var args = _factory.Last.ArgsOf(MeldObjects.UnregisterTrackObserver);
		Assert.Multiple(() =>
		{
			Assert.That(args![0], Is.EqualTo(MeldObjects.IntegrationId));
			Assert.That(args[1], Is.EqualTo("track2"));
		});
		await WaitForAsync(() => !connection.TryGetGain("track2", out _), "the evicted gain");
	}

	[Test]
	public async Task Observers_are_re_registered_after_a_reconnect()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Any(invocation => invocation.Method == MeldObjects.RegisterTrackObserver),
			"the first observer");

		_factory.Last.Drop();
		await WaitForAsync(() => _factory.Created.Count >= 2, "a reconnect attempt");
		await WaitForAsync(() => connection.IsConnected, "the reconnection to complete");

		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot()
					.Any(invocation => invocation.Method == MeldObjects.RegisterTrackObserver),
			"the observer re-registration");
	}

	[Test]
	public async Task GainUpdated_populates_the_cache_and_a_disconnect_clears_it()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		using var connection = Create(reconnectDelayMs: 30_000);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track1\""),
			FakeQWebChannelClient.Parse("0.42"),
			FakeQWebChannelClient.Parse("true"));

		await WaitForAsync(() => connection.TryGetGain("track1", out _), "the gain to be cached");
		connection.TryGetGain("track1", out var gain);
		Assert.Multiple(() =>
		{
			Assert.That(gain.Gain, Is.EqualTo(0.42));
			Assert.That(gain.Muted, Is.True);
		});

		_factory.Last.Drop();
		await WaitForAsync(() => !connection.IsConnected, "the disconnect");

		Assert.That(connection.TryGetGain("track1", out _), Is.False);
	}

	[Test]
	public async Task SetTrackMutedAsync_uses_setMuted_when_supported()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson, supportsSetMuted: true);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var task = connection.SetTrackMutedAsync("track1", true);
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot().Any(invocation => invocation.Method == MeldObjects.SetMuted),
			"the setMuted invocation");
		_factory.Last.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track1\""),
			FakeQWebChannelClient.Parse("1.0"),
			FakeQWebChannelClient.Parse("true"));

		var result = await task;
		var args = _factory.Last.ArgsOf(MeldObjects.SetMuted);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(args![0], Is.EqualTo("track1"));
			Assert.That(args[1], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task SetTrackMutedAsync_without_setMuted_is_a_noop_when_already_at_target()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson, supportsSetMuted: false);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var result = await connection.SetTrackMutedAsync("track1", false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_factory.Last.InvocationSnapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task SetTrackMutedAsync_without_setMuted_toggles_once_when_the_target_differs()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson, supportsSetMuted: false);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var task = connection.SetTrackMutedAsync("track1", true);
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot().Any(invocation => invocation.Method == MeldObjects.ToggleMute),
			"the toggleMute invocation");
		_factory.Last.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track1\""),
			FakeQWebChannelClient.Parse("1.0"),
			FakeQWebChannelClient.Parse("true"));

		await task;

		Assert.That(_factory.Last.InvocationSnapshot().Count(invocation => invocation.Method == MeldObjects.ToggleMute),
			Is.EqualTo(1));
	}

	[Test]
	public async Task SetTrackMutedAsync_on_an_unknown_track_fails_with_NotFound_naming_it()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var result = await connection.SetTrackMutedAsync("does-not-exist", true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("does-not-exist"));
		});
	}

	[Test]
	public async Task SetEffectEnabledAsync_resolves_the_scene_id_from_the_graph()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: LayerEffectSessionJson);

		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var task = connection.SetEffectEnabledAsync("effect1", true);
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot().Any(invocation => invocation.Method == MeldObjects.ToggleEffect),
			"the toggleEffect invocation");

		var args = _factory.Last.ArgsOf(MeldObjects.ToggleEffect);
		Assert.Multiple(() =>
		{
			Assert.That(args![0], Is.EqualTo("scene1"));
			Assert.That(args[1], Is.EqualTo("layer1"));
			Assert.That(args[2], Is.EqualTo("effect1"));
		});

		// Let the pending confirmation resolve so it cannot leak into a later test.
		_factory.Last.RaisePropertyUpdate(MeldObjects.Object,
			new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[MeldObjects.SessionProperty] = FakeQWebChannelClient.Parse(
					"""{"items":{"scene1":{"current":true,"index":0,"name":"Scene","staged":false,"type":"scene"},"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},"effect1":{"parent":"layer1","name":"Effect","enabled":true,"type":"effect"}}}""")
			});
		await task;
	}

	[Test]
	public async Task Reconnect_backoff_does_not_shrink_between_consecutive_failures()
	{
		_factory.Configure = client => client.ConnectException = new InvalidOperationException("refused");

		using var connection = Create(reconnectDelayMs: 200, maxReconnectDelayMs: 5_000);
		connection.Start();

		await WaitForAsync(() => _factory.CreatedAt.Count >= 4, "several failed attempts");

		var firstGap = _factory.CreatedAt[1] - _factory.CreatedAt[0];
		var laterGap = _factory.CreatedAt[3] - _factory.CreatedAt[2];

		Assert.That(laterGap, Is.GreaterThanOrEqualTo(firstGap));
	}

	[Test]
	public async Task A_good_session_resets_the_reconnect_backoff()
	{
		using var connection = Create(reconnectDelayMs: 30);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		_factory.Last.Drop();
		await WaitForAsync(() => _factory.Created.Count >= 2, "a fast reconnect");

		var gap = _factory.CreatedAt[1] - _factory.CreatedAt[0];
		Assert.That(gap, Is.LessThan(TimeSpan.FromMilliseconds(500)));
	}

	[Test]
	public async Task Three_consecutive_handshake_failures_set_NeedsSetup_but_the_pump_keeps_retrying()
	{
		_factory.Configure = client => client.Objects.Remove("meld");

		using var connection = Create(reconnectDelayMs: 10, maxReconnectDelayMs: 50, needsSetupReconnectDelayMs: 20);
		connection.Start();

		await WaitForAsync(() => connection.NeedsSetup, "NeedsSetup to be set after three failures");
		Assert.That(_factory.Created, Has.Count.GreaterThanOrEqualTo(3));

		await WaitForAsync(() => _factory.Created.Count >= 4, "the pump to keep retrying");
	}

	[Test]
	public async Task A_plain_connection_refusal_never_sets_NeedsSetup()
	{
		_factory.Configure = client => client.ConnectException = new InvalidOperationException("refused");

		using var connection = Create(reconnectDelayMs: 10);
		connection.Start();

		await WaitForAsync(() => _factory.Created.Count >= 4, "several failed attempts");

		Assert.That(connection.NeedsSetup, Is.False);
	}

	[Test]
	public async Task Dispose_mid_session_is_safe()
	{
		var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		Assert.DoesNotThrow(() => connection.Dispose());
		Assert.That(connection.IsConnected, Is.False);
	}

	[Test]
	public async Task ShowStagedSceneAsync_with_nothing_staged_fails_with_Unavailable()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var result = await connection.ShowStagedSceneAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_factory.Last.InvocationSnapshot(), Is.Empty, "nothing staged means nothing to invoke");
		});
	}

	[Test]
	public async Task SendCommandAsync_for_a_screenshot_reports_Accepted_not_Success()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var result = await connection.SendCommandAsync(MeldObjects.CommandScreenshot);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Accepted));
	}

	[Test]
	public async Task ConfirmAsync_reports_Accepted_when_the_confirmation_never_arrives_within_the_budget()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		using var connection = Create(confirmationTimeoutMs: 50);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var result = await connection.SetTrackMutedAsync("track1", true);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Accepted));
	}

	[Test]
	public async Task Dispose_while_a_confirmation_is_pending_does_not_hang_or_throw()
	{
		_factory.Configure = client => client.Objects["meld"] =
			FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: OneTrackSessionJson);

		var connection = Create(confirmationTimeoutMs: 300);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		var pending = connection.SetTrackMutedAsync("track1", true);
		await WaitForAsync(() =>
				_factory.Last.InvocationSnapshot().Any(invocation => invocation.Method == MeldObjects.SetMuted),
			"the setMuted invocation");

		Assert.DoesNotThrow(() => connection.Dispose());

		var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));
		Assert.That(completed, Is.SameAs(pending), "Dispose must not leave a pending confirmation hanging");
		Assert.DoesNotThrowAsync(async () => await pending);
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

	private MeldConnection Create(
		int reconnectDelayMs = 20,
		int? maxReconnectDelayMs = null,
		int? needsSetupReconnectDelayMs = null,
		int confirmationTimeoutMs = 500,
		Action? onVariablesChanged = null)
		=> new(_factory.Create,
			_uri,
			TimeSpan.FromMilliseconds(reconnectDelayMs),
			TimeSpan.FromMilliseconds(maxReconnectDelayMs ?? reconnectDelayMs * 4),
			TimeSpan.FromMilliseconds(confirmationTimeoutMs),
			needsSetupReconnectDelay: TimeSpan.FromMilliseconds(needsSetupReconnectDelayMs ?? reconnectDelayMs * 2),
			onVariablesChanged: onVariablesChanged);

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
		public List<FakeQWebChannelClient> Created { get; } = [];

		public List<DateTime> CreatedAt { get; } = [];

		public Action<FakeQWebChannelClient>? Configure { get; set; }

		public FakeQWebChannelClient Last => Created[^1];

		public FakeQWebChannelClient Create()
		{
			var client = new FakeQWebChannelClient();
			client.Objects["meld"] = FakeQWebChannelClient.BuildMeldObjectInfo();
			Configure?.Invoke(client);
			Created.Add(client);
			CreatedAt.Add(DateTime.UtcNow);
			return client;
		}
	}
}
