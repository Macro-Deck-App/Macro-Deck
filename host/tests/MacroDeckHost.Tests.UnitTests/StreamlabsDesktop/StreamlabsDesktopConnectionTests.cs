using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopConnectionTests
{
	private readonly List<StreamlabsDesktopConnection> _connections = [];

	private RecordingPublisher _publisher = null!;

	[SetUp]
	public void SetUp() => _publisher = new RecordingPublisher();

	[TearDown]
	public void TearDown()
	{
		foreach (var connection in _connections)
		{
			connection.Dispose();
		}

		_connections.Clear();
	}

	[Test]
	public async Task Connecting_SubscribesToEveryObservableBeforeReadingAnyState()
	{
		var client = StreamlabsJson.Seeded();
		await StartAsync(client);

		Assert.Multiple(() =>
		{
			Assert.That(client.Subscriptions,
				Is.EqualTo(StreamlabsServices.Subscriptions
					.Select(pair => StreamlabsServices.SubscriptionId(pair.Service, pair.Observable))
					.ToArray()));
			Assert.That(client.Invocations, Is.Not.Empty);
		});
	}

	[Test]
	public async Task Seeding_FillsTheSnapshot()
	{
		var client = StreamlabsJson.Seeded(streaming: "live", recording: "recording", studioMode: true);
		var connection = await StartAsync(client);

		var state = connection.State;
		Assert.Multiple(() =>
		{
			Assert.That(state.IsConnected, Is.True);
			Assert.That(state.CurrentScene, Is.EqualTo(StreamlabsJson.SceneName));
			Assert.That(state.CurrentSceneId, Is.EqualTo(StreamlabsJson.SceneId));
			Assert.That(state.SceneCount, Is.EqualTo(2));
			Assert.That(state.IsStreaming, Is.True);
			Assert.That(state.IsRecording, Is.True);
			Assert.That(state.StudioModeActive, Is.True);
			Assert.That(state.StreamingSince, Is.Not.Null);
		});
	}

	[Test]
	public async Task Connecting_PublishesConnectedOnlyAfterSeeding()
	{
		var client = StreamlabsJson.Seeded();
		await StartAsync(client);

		Assert.That(_publisher.Ids, Is.EqualTo(new[] { StreamlabsDesktopEventIds.Connected }));
	}

	[Test]
	public async Task ASceneSwitchPush_UpdatesTheSnapshotAndRaisesTheEvent()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		client.RaiseEvent(
			StreamlabsServices.SubscriptionId(StreamlabsServices.Scenes, StreamlabsServices.SceneSwitched),
			$$"""{"id":"{{StreamlabsJson.OtherSceneId}}","name":"{{StreamlabsJson.OtherSceneName}}"}""");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.CurrentScene, Is.EqualTo(StreamlabsJson.OtherSceneName));
			Assert.That(_publisher.Ids, Does.Contain(StreamlabsDesktopEventIds.SceneChanged));
			Assert.That(_publisher.Last(StreamlabsDesktopEventIds.SceneChanged)?["previousSceneName"],
				Is.EqualTo(StreamlabsJson.SceneName));
		});
	}

	[Test]
	public async Task AStreamingStatusPush_UpdatesTheSnapshot()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		RaiseStreaming(client, "live");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.IsStreaming, Is.True);
			Assert.That(connection.State.StreamingSince, Is.Not.Null);
			Assert.That(_publisher.Ids, Does.Contain(StreamlabsDesktopEventIds.StreamingStarted));
		});
	}

	[Test]
	public async Task AReconnect_KeepsTheOriginalStreamStartTime()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		RaiseStreaming(client, "live");
		var since = connection.State.StreamingSince;

		RaiseStreaming(client, "reconnecting");
		RaiseStreaming(client, "live");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.StreamingSince, Is.EqualTo(since));
			Assert.That(_publisher.Ids, Does.Not.Contain(StreamlabsDesktopEventIds.StreamingStopped));
		});
	}

	[Test]
	public async Task Disconnecting_ResetsTheStateAndReportsIt()
	{
		var client = StreamlabsJson.Seeded(streaming: "live");
		var connection = await StartAsync(client);

		client.RaiseDisconnected();
		await WaitUntil(() => !connection.State.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(connection.State, Is.EqualTo(StreamlabsDesktopState.Disconnected));
			Assert.That(_publisher.Ids, Does.Contain(StreamlabsDesktopEventIds.Disconnected));
		});
	}

	[Test]
	public async Task ARejectedToken_StopsThePumpAndRaisesTheFlag()
	{
		var attempts = 0;
		var connection = Create(() =>
		{
			attempts++;
			return new FakeStreamlabsClient
			{
				ConnectFailure = new StreamlabsAuthenticationException("nope")
			};
		});

		connection.Start();
		await WaitUntil(() => connection.NeedsAuthorization);
		await Task.Delay(150);

		Assert.Multiple(() =>
		{
			Assert.That(connection.NeedsAuthorization, Is.True);
			Assert.That(attempts, Is.EqualTo(1), "a refused token must not be retried");
		});
	}

	[Test]
	public async Task ADroppedConnection_IsRetried()
	{
		var clients = new List<FakeStreamlabsClient>();
		var connection = Create(() =>
		{
			var client = StreamlabsJson.Seeded();
			clients.Add(client);
			return client;
		});

		connection.Start();
		await WaitUntil(() => clients.Count == 1 && connection.State.IsConnected);
		clients[0].RaiseDisconnected();

		await WaitUntil(() => clients.Count > 1);
		Assert.That(clients, Has.Count.GreaterThan(1));
	}

	[TestCase("offline", true, true)]
	[TestCase("live", true, false)]
	[TestCase("reconnecting", true, false)]
	[TestCase("starting", true, false)]
	[TestCase("ending", true, false)]
	[TestCase("live", false, true)]
	[TestCase("offline", false, false)]
	[TestCase("starting", false, false)]
	[TestCase("ending", false, false)]
	public async Task StartAndStopStreaming_ToggleOnlyWhenTheStateDisagrees(
		string status,
		bool start,
		bool expectToggle)
	{
		var client = StreamlabsJson.Seeded(streaming: status);
		var connection = await StartAsync(client);
		client.Calls.Clear();

		var result = start
			? await connection.StartStreamingAsync()
			: await connection.StopStreamingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True, "a no-op still honours the user's intent");
			Assert.That(client.Calls.Any(call => call.Contains(StreamlabsServices.ToggleStreaming,
					StringComparison.Ordinal)),
				Is.EqualTo(expectToggle));
		});
	}

	[TestCase("offline", true, true)]
	[TestCase("recording", true, false)]
	[TestCase("starting", true, false)]
	[TestCase("recording", false, true)]
	[TestCase("offline", false, false)]
	[TestCase("stopping", false, false)]
	public async Task StartAndStopRecording_ToggleOnlyWhenTheStateDisagrees(
		string status,
		bool start,
		bool expectToggle)
	{
		var client = StreamlabsJson.Seeded(recording: status);
		var connection = await StartAsync(client);
		client.Calls.Clear();

		var result = start
			? await connection.StartRecordingAsync()
			: await connection.StopRecordingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(client.Calls.Any(call => call.Contains(StreamlabsServices.ToggleRecording,
					StringComparison.Ordinal)),
				Is.EqualTo(expectToggle));
		});
	}

	[Test]
	public async Task ADoublePressOnStartStreaming_TogglesOnce()
	{
		var client = StreamlabsJson.Seeded(streaming: "offline");
		var connection = await StartAsync(client);
		client.Calls.Clear();

		await connection.StartStreamingAsync();
		await connection.StartStreamingAsync();

		Assert.That(client.Calls.Count(call => call.Contains(StreamlabsServices.ToggleStreaming,
				StringComparison.Ordinal)),
			Is.EqualTo(1));
	}

	[Test]
	public async Task ToggleReplayBuffer_PicksStartOrStopFromTheLiveState()
	{
		var running = StreamlabsJson.Seeded(replayBuffer: "running");
		var runningConnection = await StartAsync(running);
		running.Calls.Clear();
		await runningConnection.ToggleReplayBufferAsync();

		var offline = StreamlabsJson.Seeded();
		var offlineConnection = await StartAsync(offline);
		offline.Calls.Clear();
		await offlineConnection.ToggleReplayBufferAsync();

		Assert.Multiple(() =>
		{
			Assert.That(running.Calls,
				Does.Contain($"{StreamlabsServices.Streaming}.{StreamlabsServices.StopReplayBuffer}"));
			Assert.That(offline.Calls,
				Does.Contain($"{StreamlabsServices.Streaming}.{StreamlabsServices.StartReplayBuffer}"));
		});
	}

	[Test]
	public async Task SetSceneAsync_ResolvesTheNameToAnId()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		var result = await connection.SetSceneAsync(StreamlabsJson.SceneName);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(client.Calls,
				Does.Contain(
					$"{StreamlabsServices.Scenes}.{StreamlabsServices.MakeSceneActive}:{StreamlabsJson.SceneId}"));
		});
	}

	[Test]
	public async Task SetSceneAsync_ReportsAnUnknownSceneAsNotFound()
	{
		var connection = await StartAsync(StreamlabsJson.Seeded());

		var result = await connection.SetSceneAsync("Nope");

		Assert.That(result.Status, Is.EqualTo(StreamlabsCommandStatus.NotFound));
	}

	[Test]
	public async Task SetSceneItemVisibleAsync_TogglesAgainstTheKnownState()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		await connection.SetSceneItemVisibleAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName, null);

		Assert.That(client.Calls,
			Does.Contain($"{StreamlabsJson.SceneItemResource}.{StreamlabsServices.SetVisibility}:False"));
	}

	[Test]
	public async Task SetSceneItemVisibleAsync_AppliesToEveryMatchingItem()
	{
		var client = StreamlabsJson.Seeded();
		client.Responses[$"{StreamlabsJson.SceneResource}.{StreamlabsServices.GetItems}"] = $$"""
			  [
			  	{"sceneId":"{{StreamlabsJson.SceneId}}","sceneItemId":"a","sourceId":"{{StreamlabsJson.CameraSourceId}}","visible":true},
			  	{"sceneId":"{{StreamlabsJson.SceneId}}","sceneItemId":"b","sourceId":"{{StreamlabsJson.CameraSourceId}}","visible":false}
			  ]
			  """;

		var connection = await StartAsync(client);
		await connection.SetSceneItemVisibleAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName, null);

		Assert.That(
			client.Calls.Count(call => call.Contains(StreamlabsServices.SetVisibility, StringComparison.Ordinal)),
			Is.EqualTo(2));
	}

	[TestCase(50d, "0.5")]
	[TestCase(150d, "1")]
	[TestCase(-10d, "0")]
	public async Task SetAudioVolumePercentAsync_ClampsOntoTheDeflectionRange(double percent, string expected)
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		await connection.SetAudioVolumePercentAsync(StreamlabsJson.MicSourceName, percent);

		Assert.That(client.Calls,
			Does.Contain($"{StreamlabsJson.MicResource}.{StreamlabsServices.SetDeflection}:{expected}"));
	}

	[Test]
	public async Task AdjustAudioVolumePercentAsync_ReadsBeforeItWrites()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);
		client.Calls.Clear();

		await connection.AdjustAudioVolumePercentAsync(StreamlabsJson.MicSourceName, -30d);

		Assert.Multiple(() =>
		{
			Assert.That(client.Calls[0], Is.EqualTo($"{StreamlabsJson.MicResource}.{StreamlabsServices.GetModel}"));
			Assert.That(client.Calls,
				Does.Contain($"{StreamlabsJson.MicResource}.{StreamlabsServices.SetDeflection}:0.5"));
		});
	}

	[Test]
	public async Task SetAudioMutedAsync_ReportsAnUnknownSourceAsNotFound()
	{
		var connection = await StartAsync(StreamlabsJson.Seeded());

		var result = await connection.SetAudioMutedAsync("Nope", true);

		Assert.That(result.Status, Is.EqualTo(StreamlabsCommandStatus.NotFound));
	}

	[Test]
	public async Task ARefusedCall_IsReportedAsRejected()
	{
		var client = StreamlabsJson.Seeded();
		client.Failures[$"{StreamlabsServices.Streaming}.{StreamlabsServices.SaveReplay}"] =
			new StreamlabsRpcException("Replay buffer is not running");

		var connection = await StartAsync(client);
		var result = await connection.SaveReplayAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(StreamlabsCommandStatus.Rejected));
			Assert.That(result.Message, Does.Contain("Replay buffer"));
		});
	}

	[Test]
	public async Task CommandsWhileDisconnected_ReportNotConnectedRatherThanThrowing()
	{
		var connection = Create(() => new FakeStreamlabsClient
		{
			ConnectFailure = new StreamlabsAuthenticationException("nope")
		});

		Assert.Multiple(async () =>
		{
			Assert.That((await connection.SetSceneAsync("Gameplay")).Status,
				Is.EqualTo(StreamlabsCommandStatus.NotConnected));
			Assert.That((await connection.SaveReplayAsync()).Status,
				Is.EqualTo(StreamlabsCommandStatus.NotConnected));
			Assert.That(await connection.GetAudioVolumePercentAsync("Mic/Aux"), Is.Null);
			Assert.That(await connection.GetSceneItemVisibleAsync("Gameplay", "Webcam"), Is.Null);
		});
	}

	[Test]
	public async Task AnItemUpdatedPush_PatchesVisibilityAndRaisesTheEvent()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		await connection.GetSceneItemNamesAsync(StreamlabsJson.SceneName);

		client.RaiseEvent(StreamlabsServices.SubscriptionId(StreamlabsServices.Scenes, StreamlabsServices.ItemUpdated),
			$$"""{"sceneId":"{{StreamlabsJson.SceneId}}","sceneItemId":"{{StreamlabsJson.CameraItemId}}","sourceId":"{{StreamlabsJson.CameraSourceId}}","visible":false}""");

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Does.Contain(StreamlabsDesktopEventIds.SourceVisibilityChanged));
			var payload = _publisher.Last(StreamlabsDesktopEventIds.SourceVisibilityChanged);
			Assert.That(payload?["sourceName"], Is.EqualTo(StreamlabsJson.CameraSourceName));
			Assert.That(payload?["sceneName"], Is.EqualTo(StreamlabsJson.SceneName));
			Assert.That(payload?["visible"], Is.EqualTo(false));
		});
	}

	[Test]
	public async Task ASourceUpdatedPushWithMute_RaisesTheMuteEventWithoutARoundTrip()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);
		await connection.GetAudioSourceNamesAsync();
		client.Calls.Clear();

		client.RaiseEvent(
			StreamlabsServices.SubscriptionId(StreamlabsServices.Sources, StreamlabsServices.SourceUpdated),
			$$"""{"sourceId":"{{StreamlabsJson.MicSourceId}}","name":"{{StreamlabsJson.MicSourceName}}","muted":true}""");

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Does.Contain(StreamlabsDesktopEventIds.SourceMuteChanged));
			Assert.That(_publisher.Last(StreamlabsDesktopEventIds.SourceMuteChanged)?["muted"], Is.EqualTo(true));
			Assert.That(client.Calls, Is.Empty, "the payload already carried the mute state");
		});
	}

	[Test]
	public async Task ASourceUpdatedPushWithoutMute_ReadsItBack()
	{
		var client = StreamlabsJson.Seeded();
		client.Responses[$"{StreamlabsJson.MicResource}.{StreamlabsServices.GetModel}"] =
			StreamlabsJson.AudioSource(muted: true);

		var connection = await StartAsync(client);
		await connection.GetAudioSourceNamesAsync();

		client.RaiseEvent(
			StreamlabsServices.SubscriptionId(StreamlabsServices.Sources, StreamlabsServices.SourceUpdated),
			$$"""{"sourceId":"{{StreamlabsJson.MicSourceId}}","name":"{{StreamlabsJson.MicSourceName}}"}""");

		await WaitUntil(() => _publisher.Ids.Contains(StreamlabsDesktopEventIds.SourceMuteChanged));

		Assert.That(_publisher.Last(StreamlabsDesktopEventIds.SourceMuteChanged)?["muted"], Is.EqualTo(true));
	}

	[Test]
	public async Task AMalformedPush_DoesNotEndTheSession()
	{
		var client = StreamlabsJson.Seeded();
		var connection = await StartAsync(client);

		client.RaiseEvent(
			StreamlabsServices.SubscriptionId(StreamlabsServices.Scenes, StreamlabsServices.SceneSwitched),
			"""{"unexpected":true}""");

		Assert.That(connection.State.IsConnected, Is.True);
	}

	private static void RaiseStreaming(FakeStreamlabsClient client, string status)
		=> client.RaiseEvent(
			StreamlabsServices.SubscriptionId(StreamlabsServices.Streaming, StreamlabsServices.StreamingStatusChange),
			$"\"{status}\"");

	[Test]
	public async Task Connecting_asks_for_an_eager_variable_refresh()
	{
		var requests = 0;
		var client = new FakeStreamlabsClient();
		var connection = Create(() => client, () => Interlocked.Increment(ref requests));
		_connections.Add(connection);
		connection.Start();

		await WaitUntil(() => connection.State.IsConnected);
		await WaitUntil(() => Volatile.Read(ref requests) > 0);

		Assert.That(Volatile.Read(ref requests), Is.GreaterThan(0));
	}

	private StreamlabsDesktopConnection Create(
		Func<IStreamlabsClient> factory,
		Action? onVariablesChanged = null)
	{
		var connection = new StreamlabsDesktopConnection(factory,
			new StreamlabsDesktopEndpoint("127.0.0.1", 59650),
			"token",
			new StreamlabsDesktopEventEmitter(_publisher),
			TimeSpan.FromMilliseconds(20),
			onVariablesChanged: onVariablesChanged);

		_connections.Add(connection);
		return connection;
	}

	private async Task<StreamlabsDesktopConnection> StartAsync(FakeStreamlabsClient client)
	{
		var connection = Create(() => client);
		connection.Start();
		await WaitUntil(() => connection.State.IsConnected);
		return connection;
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 200 && !condition(); attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "the expected state was never reached");
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		private readonly List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> _published = [];

		public IReadOnlyList<string> Ids
		{
			get
			{
				lock (_published)
				{
					return _published.Select(entry => entry.EventId).ToList();
				}
			}
		}

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		{
			lock (_published)
			{
				_published.Add((eventId, parameters));
			}
		}

		public IReadOnlyDictionary<string, object?>? Last(string eventId)
		{
			lock (_published)
			{
				return _published.LastOrDefault(entry => entry.EventId == eventId).Parameters;
			}
		}
	}
}
