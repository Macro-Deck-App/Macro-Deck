using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamRelayBrokerTests
{
	private const string Front = VideoStreamWorld.BuiltIn + "::front";
	private const string PluginFront = VideoStreamWorld.PluginId + "::front";
	private const string ProviderHost = "camera.local";

	public enum Phase
	{
		Open,
		Update,
		Resume
	}

	public enum Breakage
	{
		SchemeTheHostDoesNotFetch,
		TransportTheConsumerDidNotAccept,
		TransportTheHostDoesNotPlay
	}

	public enum Ending
	{
		ConsumerCloses,
		LeaseExpires,
		ProviderCloses,
		RegistrationChanges,
		ConnectionCloses,
		PluginSessionEnds,
		HostShutsDown
	}

	private static string TokenOf(string relayUrl)
		=> relayUrl[VideoStreamRelay.PathPrefix.Length..].Split('/')[0];

	private static async Task<(VideoStreamWorld World, string SessionId)> OpenBuiltInAsync()
	{
		var world = new VideoStreamWorld();
		await world.Context().RegisterProviderAsync(new ScriptedVideoProvider("front", "main"));
		var sessionId = world.Open(Front, "main").SessionId;
		await world.WaitForActiveAsync(sessionId);
		return (world, sessionId);
	}

	private static Task WaitForStateAsync(VideoStreamWorld world, string sessionId, VideoStreamSessionState state)
		=> VideoStreamWorld.WaitForAsync(() => world.Publisher.Of<VideoStreamSessionChangedNotification>()
				.Any(changed => changed.SessionId == sessionId && changed.State == state),
			$"session {sessionId} never became {state}");

	private static int Closes(VideoStreamWorld world)
		=> world.Plugin.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose)
			.Count;

	[Test]
	public async Task The_consumer_only_hears_of_host_urls_across_open_update_suspend_and_resume_and_the_path_stays_the_same()
	{
		var (world, sessionId) = await OpenBuiltInAsync();
		using var _w = world;

		await world.Context().UpdateSessionAsync(sessionId,
			VideoStreamSessionState.Active,
			VideoStreamSessionDescription.Mjpeg("http://camera.local:8080/stream.mjpg?channel=2"));
		world.Broker.SuspendSession("ui-1", sessionId);
		await WaitForStateAsync(world, sessionId, VideoStreamSessionState.Suspended);
		world.Broker.ResumeSession("ui-1", sessionId);
		await VideoStreamWorld.WaitForAsync(
			() => world.Publisher.Of<VideoStreamSessionChangedNotification>().Count == 4,
			"the resume never completed");

		var changes = world.Publisher.Of<VideoStreamSessionChangedNotification>();
		Assert.Multiple(() =>
		{
			Assert.That(changes.Select(change => change.Description!.Url), Has.All.StartWith(VideoStreamRelay.PathPrefix));
			Assert.That(changes.Select(change => change.Description!.Url), Has.None.Contains(ProviderHost));
			Assert.That(changes.Select(change => TokenOf(change.Description!.Url)).Distinct().Count(), Is.EqualTo(1));
			Assert.That(changes.Select(change => change.Description!.Transport),
				Is.EqualTo(new[] { "hls", "mjpeg", "mjpeg", "hls" }));
			Assert.That(changes[1].Description!.Url, Does.EndWith("/stream.mjpg?channel=2"));
		});
	}

	[Test]
	public async Task The_relay_serves_the_source_the_provider_last_named_under_the_token_the_consumer_holds()
	{
		var (world, sessionId) = await OpenBuiltInAsync();
		using var _w = world;
		var token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);

		await world.Context().UpdateSessionAsync(sessionId,
			VideoStreamSessionState.Active,
			VideoStreamSessionDescription.Mjpeg("http://camera.local:8080/stream.mjpg"));
		var acquisition = world.Relay.Acquire(token, out var lease);
		using var __ = lease;

		Assert.Multiple(() =>
		{
			Assert.That(acquisition, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
			Assert.That(lease!.Transport, Is.EqualTo("mjpeg"));
			Assert.That(lease.Upstream, Is.EqualTo(new Uri("http://camera.local:8080/stream.mjpg")));
		});
	}

	[Test]
	public async Task Suspending_a_session_aborts_its_requests_and_resuming_it_serves_the_same_path_again()
	{
		var (world, sessionId) = await OpenBuiltInAsync();
		using var _w = world;
		var token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);
		Assert.That(world.Relay.Acquire(token, out var inFlight), Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
		using var __ = inFlight;

		world.Broker.SuspendSession("ui-1", sessionId);
		await WaitForStateAsync(world, sessionId, VideoStreamSessionState.Suspended);
		var whileSuspended = world.Relay.Acquire(token, out _);
		world.Broker.ResumeSession("ui-1", sessionId);
		await VideoStreamWorld.WaitForAsync(
			() => world.Publisher.Of<VideoStreamSessionChangedNotification>().Count == 3,
			"the resume never completed");
		var afterResume = world.Relay.Acquire(token, out var resumed);
		using var ___ = resumed;

		Assert.Multiple(() =>
		{
			Assert.That(inFlight!.Aborted.IsCancellationRequested, Is.True);
			Assert.That(whileSuspended, Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(afterResume, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
		});
	}

	[Test]
	public async Task The_provider_is_only_offered_the_transports_the_host_plays_in_the_clients_order()
	{
		using var world = new VideoStreamWorld();
		var provider = new ScriptedVideoProvider("front", "main");
		await world.Context().RegisterProviderAsync(provider);

		var sessionId = world.Broker.OpenSession("ui-1", Front, "main", ["webrtc", "mjpeg", "hls", "mjpeg"]).SessionId;
		await world.WaitForActiveAsync(sessionId);

		Assert.That(provider.Opens.Single().AcceptedTransports, Is.EqualTo(new[] { "mjpeg", "hls" }));
	}

	[Test]
	public async Task An_open_offering_only_transports_the_host_does_not_play_is_refused_without_calling_the_provider()
	{
		using var world = new VideoStreamWorld();
		var provider = new ScriptedVideoProvider("front", "main");
		await world.Context().RegisterProviderAsync(provider);

		var refused = Assert.Throws<VideoStreamBrokerException>(
			() => world.Broker.OpenSession("ui-1", Front, "main", ["webrtc", "whep"]));
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(refused!.Error, Is.EqualTo(VideoStreamError.TransportNotAccepted));
			Assert.That(provider.Opens, Is.Empty);
			Assert.That(world.Publisher.Of<VideoStreamSessionChangedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task A_provider_update_to_active_on_a_suspended_session_serves_the_same_path_again()
	{
		var (world, sessionId) = await OpenBuiltInAsync();
		using var _w = world;
		var token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);
		world.Broker.SuspendSession("ui-1", sessionId);
		await WaitForStateAsync(world, sessionId, VideoStreamSessionState.Suspended);

		await world.Context().UpdateSessionAsync(sessionId, VideoStreamSessionState.Active);
		var acquisition = world.Relay.Acquire(token, out var lease);
		using var __ = lease;

		Assert.Multiple(() =>
		{
			Assert.That(acquisition, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
			Assert.That(lease!.Transport, Is.EqualTo("hls"));
			Assert.That(lease.Upstream, Is.EqualTo(new Uri("https://camera.local/main")));
		});
	}

	[Test]
	public async Task An_update_that_leaves_a_session_suspended_keeps_its_path_suspended_even_with_a_new_source()
	{
		var (world, sessionId) = await OpenBuiltInAsync();
		using var _w = world;
		var token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);
		world.Broker.SuspendSession("ui-1", sessionId);
		await WaitForStateAsync(world, sessionId, VideoStreamSessionState.Suspended);

		await world.Context().UpdateSessionAsync(sessionId, VideoStreamSessionState.Suspended);
		var plain = world.Relay.Acquire(token, out _);
		await world.Context().UpdateSessionAsync(sessionId,
			VideoStreamSessionState.Suspended,
			VideoStreamSessionDescription.Hls("https://camera.local/elsewhere"));
		var withSource = world.Relay.Acquire(token, out _);

		Assert.Multiple(() =>
		{
			Assert.That(plain, Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(withSource, Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
		});
	}

	[Test]
	public async Task A_description_the_host_cannot_relay_ends_the_session_and_the_provider_hears_of_it_exactly_once(
		[Values] Phase phase,
		[Values] Breakage breakage)
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		IReadOnlyList<string> accepted = breakage switch
		{
			Breakage.TransportTheConsumerDidNotAccept => ["hls"],
			Breakage.TransportTheHostDoesNotPlay => ["hls", "webrtc"],
			_ => ["hls", "mjpeg"]
		};
		var bad = breakage switch
		{
			Breakage.SchemeTheHostDoesNotFetch => new VideoStreamSessionDescriptionDto
			{
				Transport = "hls", Url = "ftp://camera.local/live"
			},
			Breakage.TransportTheConsumerDidNotAccept => new VideoStreamSessionDescriptionDto
			{
				Transport = "mjpeg", Url = "http://camera.local/live.mjpg"
			},
			_ => new VideoStreamSessionDescriptionDto { Transport = "webrtc", Url = "https://camera.local/live" }
		};
		world.Plugin.Handler = (operation, arguments) => Task.FromResult<object?>(operation switch
		{
			CapabilityOperations.VideoStreamProvider.SessionOpen when phase == Phase.Open =>
				new VideoStreamSessionOpenResult { Description = bad, RegistrationId = "r1" },
			CapabilityOperations.VideoStreamProvider.SessionResume when phase == Phase.Resume =>
				new VideoStreamSessionResumeResult { Description = bad },
			_ => world.Plugin.Default(operation, arguments)
		});
		await world.AttachPluginAsync();

		var sessionId = world.Broker.OpenSession("ui-1", PluginFront, "main", accepted).SessionId;
		string? token = null;
		if (phase != Phase.Open)
		{
			await world.WaitForActiveAsync(sessionId);
			token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);
		}

		switch (phase)
		{
			case Phase.Update:
				world.Broker.ApplyProviderUpdate(VideoStreamWorld.PluginId,
					sessionId,
					VideoStreamSessionState.Active,
					bad,
					VideoStreamSessionReason.None,
					null);
				break;
			case Phase.Resume:
				world.Broker.SuspendSession("ui-1", sessionId);
				await WaitForStateAsync(world, sessionId, VideoStreamSessionState.Suspended);
				world.Broker.ResumeSession("ui-1", sessionId);
				break;
		}

		await world.WaitForClosedAsync(sessionId);
		await VideoStreamWorld.WaitForAsync(() => Closes(world) >= 1, "the provider never heard of the close");
		await Task.Delay(100);

		var closed = world.Publisher.Of<VideoStreamSessionClosedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(closed.Reason, Is.EqualTo(VideoStreamSessionReason.Failed));
			Assert.That(closed.Error,
				Is.EqualTo(breakage == Breakage.SchemeTheHostDoesNotFetch
					? VideoStreamError.Failed
					: VideoStreamError.TransportNotAccepted));
			Assert.That(Closes(world), Is.EqualTo(1));
			Assert.That(world.Publisher.Of<VideoStreamSessionChangedNotification>().Select(change => change.Description?.Url),
				Has.None.Contains(ProviderHost));
			if (token is not null)
			{
				Assert.That(world.Relay.Acquire(token, out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			}
		});
	}

	[Test]
	public async Task A_relayed_path_dies_with_the_session_however_the_session_ends([Values] Ending ending)
	{
		var time = ending == Ending.LeaseExpires ? new ManualTimeProvider() : null;
		using var world = new VideoStreamWorld(time: time);
		world.Plugin.AddProvider("front", "r1", "main");
		var (pluginSession, connection) = await world.ConnectPluginAsync();
		await world.Registry.AttachRemoteAsync(VideoStreamWorld.PluginId, pluginSession, connection);
		var sessionId = world.Open(PluginFront, "main").SessionId;
		await world.WaitForActiveAsync(sessionId);
		var token = TokenOf(world.Publisher.Of<VideoStreamSessionChangedNotification>().Single().Description!.Url);
		Assert.That(world.Relay.Acquire(token, out var inFlight), Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
		using var _i = inFlight;

		switch (ending)
		{
			case Ending.ConsumerCloses:
				world.Broker.CloseSession("ui-1", sessionId);
				break;
			case Ending.LeaseExpires:
				time!.Advance(TimeSpan.FromSeconds(30));
				time.Advance(TimeSpan.FromSeconds(30));
				break;
			case Ending.ProviderCloses:
				world.Broker.ApplyProviderClose(VideoStreamWorld.PluginId,
					sessionId,
					VideoStreamSessionReason.SourceLost,
					null);
				break;
			case Ending.RegistrationChanges:
				world.Plugin.AddProvider("front", "r2", "main");
				await world.Registry.RequestProvidersPullAsync(VideoStreamWorld.PluginId, pluginSession, connection);
				break;
			case Ending.ConnectionCloses:
				world.Broker.CloseConnection("ui-1", VideoStreamSessionReason.ConsumerDisconnected);
				break;
			case Ending.PluginSessionEnds:
				await world.PluginSessions.Terminate(pluginSession, 4000, "gone");
				break;
			case Ending.HostShutsDown:
				await world.Broker.ShutdownAsync(TimeSpan.FromSeconds(5));
				break;
		}

		await world.WaitForClosedAsync(sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(world.Relay.Acquire(token, out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(inFlight!.Aborted.IsCancellationRequested, Is.True);
		});
	}
}
