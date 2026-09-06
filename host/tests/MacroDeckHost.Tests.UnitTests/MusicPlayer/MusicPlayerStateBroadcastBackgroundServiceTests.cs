using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.MusicPlayer;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerStateBroadcastBackgroundServiceTests
{
	private static readonly string[] _spotifyInstanceId = ["app.macro-deck.spotify::entry"];
	private static readonly string[] _bothInstanceIds = ["sinusbot::a", "spotify::b"];
	private static readonly string[] _oneTrackName = ["Song"];

	[Test]
	public async Task Tick_broadcasts_the_instance_list_so_a_late_provider_reaches_connected_clients()
	{
		var registry = new FakeMusicPlayerRegistry();
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None);
		Assert.That(InstanceBroadcasts(transport), Is.Empty, "an initially empty list must not be announced");

		registry.Add("app.macro-deck.spotify::entry", "Spotify");
		await service.Tick(CancellationToken.None);

		var announced = InstanceBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(announced, Has.Count.EqualTo(1));
			Assert.That(announced[0].Instances.Select(i => i.InstanceId),
				Is.EqualTo(_spotifyInstanceId));
			Assert.That(TestLocalization.Resolve(announced[0].Instances[0].ProviderName), Is.EqualTo("Spotify"));
		});
	}

	[Test]
	public void The_instances_event_keeps_the_name_the_client_subscribes_to()
	{
		Assert.That(typeof(MusicPlayerInstancesChangedNotification).Name,
			Is.EqualTo("MusicPlayerInstancesChangedNotification"),
			"music-player.service.ts subscribes to this exact string");
	}

	// The poll is the only thing that reads a provider. GetMusicPlayerStateRequestMessageHandler serves
	// the hub from this cache instead of calling the provider itself, so a tick that broadcasts but
	// does not record would leave every on-demand read answering "unknown" forever (issue #189).
	[Test]
	public async Task Tick_records_each_polled_state_so_the_hub_read_never_calls_a_provider()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify").State = Playing("Song");
		var cache = new MusicPlayerStateCache();
		var service = CreateService(registry, new RecordingUiTransport(), cache);

		await service.Tick(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetState("spotify::a"), Is.Not.Null);
			Assert.That(cache.GetState("spotify::a")?.TrackName, Is.EqualTo("Song"));
		});
	}

	[Test]
	public async Task Tick_forgets_the_cached_state_of_an_instance_that_disappeared()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify").State = Playing("Song");
		var cache = new MusicPlayerStateCache();
		var service = CreateService(registry, new RecordingUiTransport(), cache);
		await service.Tick(CancellationToken.None);

		registry.Clear();
		registry.Add("spotify::b", "Spotify");
		await service.Tick(CancellationToken.None);

		Assert.That(cache.GetState("spotify::a"), Is.Null);
	}

	[Test]
	public async Task Tick_does_not_re_send_an_unchanged_instance_list()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.That(InstanceBroadcasts(transport), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Tick_holds_a_transient_empty_instance_list_during_a_reinitialize_gap()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify").State = Playing("Song");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None); // announces [a], playing
		registry.Clear(); // ShutdownAsync ran, ConnectFromConfig has not finished yet
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);
		registry.Add("spotify::a", "Spotify").State = Playing("Song"); // reinit finished, same instance
		await service.Tick(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(InstanceBroadcasts(transport),
				Has.Count.EqualTo(1),
				"the transient empty must never reach the client");
			Assert.That(StateBroadcasts(transport).Any(s => s.State.InstanceId == "spotify::a" && !s.State.IsConnected),
				Is.False,
				"a held instance must not be reported as disconnected");
		});
	}

	[Test]
	public async Task Tick_broadcasts_the_empty_list_once_the_empty_is_confirmed()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None);
		registry.Clear();
		for (var i = 0; i < 10; i++)
		{
			await service.Tick(CancellationToken.None);
		}

		var announced = InstanceBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(announced, Has.Count.EqualTo(2));
			Assert.That(announced[^1].Instances, Is.Empty);
		});
	}

	[Test]
	public async Task The_snapshot_keeps_the_announced_list_through_a_reinitialize_gap()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("app.macro-deck.spotify::entry", "Spotify");
		var snapshot = new MusicPlayerInstancesSnapshot();
		var service = CreateService(registry, new RecordingUiTransport(), instancesSnapshot: snapshot);

		await service.Tick(CancellationToken.None);
		registry.Clear(); // reinit gap: registry momentarily empty, list held
		await service.Tick(CancellationToken.None);

		Assert.That(snapshot.Instances.Select(i => i.InstanceId), Is.EqualTo(_spotifyInstanceId));
	}

	[Test]
	public async Task The_resync_tick_re_announces_the_unchanged_list_and_states()
	{
		var registry = new FakeMusicPlayerRegistry();
		registry.Add("spotify::a", "Spotify").State = Playing("Song");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		for (var i = 0; i < 31; i++)
		{
			await service.Tick(CancellationToken.None);
		}

		Assert.Multiple(() =>
		{
			Assert.That(InstanceBroadcasts(transport),
				Has.Count.EqualTo(2),
				"the unchanged list must go out again on the resync tick");
			Assert.That(StateBroadcasts(transport),
				Has.Count.EqualTo(2),
				"the unchanged state must go out again on the resync tick");
		});
	}

	[Test]
	public async Task Tick_keeps_the_last_state_when_a_provider_stops_answering()
	{
		var registry = new FakeMusicPlayerRegistry();
		var player = registry.Add("spotify::a", "Spotify");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		player.State = Playing("Song");
		await service.Tick(CancellationToken.None);

		player.Throw = new HttpRequestException("connection reset");
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		var states = StateBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(states, Has.Count.EqualTo(1), "a failed read must not push a replacement state");
			Assert.That(states[0].State.TrackName, Is.EqualTo("Song"));
		});
	}

	[Test]
	public async Task Tick_polls_instances_concurrently_so_a_slow_provider_cannot_stall_the_others()
	{
		var registry = new FakeMusicPlayerRegistry();
		var slow = registry.Add("sinusbot::a", "SinusBot");
		var fast = registry.Add("spotify::b", "Spotify");
		slow.Gate = new TaskCompletionSource();
		fast.State = Playing("Song");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		var tick = service.Tick(CancellationToken.None);

		await fast.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		slow.Gate.SetResult();
		await tick.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(StateBroadcasts(transport).Select(s => s.State.InstanceId),
			Is.EquivalentTo(_bothInstanceIds));
	}

	[Test]
	public async Task Tick_retries_a_state_whose_push_failed()
	{
		var registry = new FakeMusicPlayerRegistry();
		var player = registry.Add("spotify::a", "Spotify");
		player.State = Playing("Song");
		var transport = new RecordingUiTransport { FailSends = true };
		var service = CreateService(registry, transport);

		Assert.That(async () => await service.Tick(CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
		transport.FailSends = false;
		await service.Tick(CancellationToken.None);

		Assert.That(StateBroadcasts(transport).Select(s => s.State.TrackName), Is.EqualTo(_oneTrackName));
	}

	[Test]
	public async Task A_poll_nudge_triggers_an_extra_poll_before_the_next_interval()
	{
		var registry = new FakeMusicPlayerRegistry();
		var player = registry.Add("spotify::a", "Spotify");
		player.State = Playing("Song");
		var nudge = new ManualNudge();
		var transport = new RecordingUiTransport();
		var service = CreateService(registry,
			transport,
			pollNudge: nudge,
			pollInterval: TimeSpan.FromSeconds(60));

		await service.StartAsync(CancellationToken.None);
		try
		{
			await WaitFor(() => player.Polls >= 1);

			player.State = Playing("Next Song");
			nudge.Fire();
			await WaitFor(() => player.Polls >= 2);
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}

		Assert.That(StateBroadcasts(transport).Any(s => s.State.TrackName == "Next Song"), Is.True);
	}

	private static async Task WaitFor(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 250 && !condition(); attempt++)
		{
			await Task.Delay(20);
		}

		Assert.That(condition(), Is.True, "the condition was not reached in time");
	}

	private sealed class ManualNudge : IMusicPlayerPollNudge
	{
		private volatile TaskCompletionSource _due = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Due => _due.Task;

		public void Fire() => _due.TrySetResult();

		public void NoteActionExecuted(string integrationId)
		{
		}

		public void Rearm() => _due = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	[Test]
	public async Task A_connection_flip_is_logged_once_with_its_direction()
	{
		var sink = new CollectingSink();
		var registry = new FakeMusicPlayerRegistry();
		var player = registry.Add("spotify::a", "Spotify");
		player.State = Playing("Song");
		var service = CreateService(registry, new RecordingUiTransport(), logger: Collecting(sink));

		await service.Tick(CancellationToken.None);
		player.State = MusicPlayerState.Disconnected;
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e => e.MessageTemplate.Text.Contains("initial state")),
				Is.EqualTo(1));
			Assert.That(sink.Events.Count(e => e.MessageTemplate.Text.Contains("{Previous} -> {Current}")),
				Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Poll_failures_log_one_warning_onset_and_one_recovery()
	{
		var sink = new CollectingSink();
		var registry = new FakeMusicPlayerRegistry();
		var player = registry.Add("spotify::a", "Spotify");
		player.State = Playing("Song");
		var service = CreateService(registry, new RecordingUiTransport(), logger: Collecting(sink));
		await service.Tick(CancellationToken.None);

		player.Throw = new HttpRequestException("connection reset");
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);
		player.Throw = null;
		await service.Tick(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.Contains("started failing")),
				Is.EqualTo(1));
			Assert.That(sink.Events.Count(e =>
					e.Level == LogEventLevel.Information && e.MessageTemplate.Text.Contains("recovered after")),
				Is.EqualTo(1));
		});
	}

	private static MusicPlayerState Playing(string trackName)
		=> new()
		{
			IsConnected = true,
			PlaybackState = PlaybackState.Playing,
			TrackName = trackName
		};

	private static MusicPlayerStateBroadcastBackgroundService CreateService(
		IMusicPlayerRegistry registry,
		RecordingUiTransport transport,
		IMusicPlayerStateCache? stateCache = null,
		ILogger? logger = null,
		IMusicPlayerPollNudge? pollNudge = null,
		TimeSpan? pollInterval = null,
		IMusicPlayerInstancesSnapshot? instancesSnapshot = null,
		IMusicPlayerStateNotifier? notifier = null)
		=> new(new StartedHostLifetime(),
			registry,
			transport,
			new MusicPlayerEventProvider(new Triggers.RecordingEventBus()),
			stateCache ?? new MusicPlayerStateCache(),
			pollNudge ?? new NeverNudge(),
			instancesSnapshot ?? new MusicPlayerInstancesSnapshot(),
			notifier ?? new MusicPlayerStateNotifier(),
			logger ?? SilentLogger(),
			pollInterval: pollInterval);

	private sealed class NeverNudge : IMusicPlayerPollNudge
	{
		public Task Due { get; } = new TaskCompletionSource().Task;

		public void NoteActionExecuted(string integrationId)
		{
		}

		public void Rearm()
		{
		}
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private static Logger Collecting(CollectingSink sink)
		=> new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}

	private static List<MusicPlayerInstancesChangedNotification> InstanceBroadcasts(RecordingUiTransport transport)
		=> transport.Broadcasts.OfType<MusicPlayerInstancesChangedNotification>().ToList();

	private static List<MusicPlayerStateChangedNotification> StateBroadcasts(RecordingUiTransport transport)
		=> transport.Broadcasts.OfType<MusicPlayerStateChangedNotification>().ToList();

	private sealed class FakeMusicPlayerRegistry : IMusicPlayerRegistry
	{
		private readonly List<MusicPlayerInstanceDescriptor> _descriptors = [];
		private readonly Dictionary<string, FakeMusicPlayer> _players = new(StringComparer.Ordinal);

		public IMusicPlayer? DefaultPlayer => _descriptors.Count > 0 ? GetPlayer(_descriptors[0].InstanceId) : null;

		public FakeMusicPlayer Add(string instanceId, string providerName)
		{
			_descriptors.Add(new MusicPlayerInstanceDescriptor(instanceId,
				instanceId.Split("::")[0],
				providerName,
				instanceId,
				false));
			var player = new FakeMusicPlayer();
			_players[instanceId] = player;
			return player;
		}

		public void Clear()
		{
			_descriptors.Clear();
			_players.Clear();
		}

		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => _descriptors.ToList();

		public IMusicPlayer? GetPlayer(string instanceId) => _players.GetValueOrDefault(instanceId);
	}

	private sealed class FakeMusicPlayer : IMusicPlayer
	{
		private int _polls;

		public MusicPlayerState State { get; set; } = MusicPlayerState.Disconnected;

		public Exception? Throw { get; set; }

		public TaskCompletionSource? Gate { get; set; }

		public TaskCompletionSource Entered { get; } = new();

		public int Polls => Volatile.Read(ref _polls);

		public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _polls);
			Entered.TrySetResult();

			if (Gate is not null)
			{
				await Gate.Task.WaitAsync(cancellationToken);
			}

			if (Throw is not null)
			{
				throw Throw;
			}

			return State;
		}

		public Task<MusicPlayerArtwork?> GetArtworkAsync(
			string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
