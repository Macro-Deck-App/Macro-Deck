using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamSessionBrokerTests
{
	private const string Front = VideoStreamWorld.BuiltIn + "::front";

	private static async Task<(VideoStreamWorld World, ScriptedVideoProvider Provider)> BuiltInAsync(
		TimeProvider? time = null,
		TimeSpan? inProcessTimeout = null)
	{
		var world = new VideoStreamWorld(time: time, inProcessTimeout: inProcessTimeout);
		var provider = new ScriptedVideoProvider("front", "main", "hung");
		await world.Context().RegisterProviderAsync(provider);
		return (world, provider);
	}

	private static async Task<string> OpenActiveAsync(VideoStreamWorld world,
		string providerId = Front,
		string streamId = "main",
		string connectionId = "ui-1")
	{
		var ticket = world.Open(providerId, streamId, connectionId);
		await world.WaitForActiveAsync(ticket.SessionId);
		return ticket.SessionId;
	}

	[Test]
	public async Task An_open_answers_at_once_and_the_session_turns_active_with_a_host_url_for_the_providers_stream()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;

		var ticket = world.Open(Front, "main");
		await world.WaitForActiveAsync(ticket.SessionId);

		var active = world.Publisher.Of<VideoStreamSessionChangedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(ticket.Revision, Is.Zero);
			Assert.That(ticket.State, Is.EqualTo(VideoStreamSessionState.Opening));
			Assert.That(active.ConnectionId, Is.EqualTo("ui-1"));
			Assert.That(active.Revision, Is.EqualTo(1));
			Assert.That(active.Description!.Transport, Is.EqualTo("hls"));
			Assert.That(active.Description.Url, Does.StartWith(VideoStreamRelay.PathPrefix).And.EndsWith("/main"));
			Assert.That(active.Description.Url, Does.Not.Contain("camera.local"));
			Assert.That(provider.Opens.Single().AcceptedTransports, Is.EqualTo(new[] { "hls", "mjpeg" }));
		});
	}

	[Test]
	public async Task Another_connection_can_neither_keep_alive_nor_close_a_session_it_does_not_own()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var sessionId = await OpenActiveAsync(world);

		var keepAlive = Assert.Throws<VideoStreamBrokerException>(() => world.Broker.KeepAliveSession("ui-2", sessionId));
		world.Broker.CloseSession("ui-2", sessionId);
		world.Broker.KeepAliveSession("ui-1", sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(keepAlive!.Error, Is.EqualTo(VideoStreamError.UnknownSession));
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>(), Is.Empty);
			Assert.That(provider.Closes, Is.Empty);
		});
	}

	[Test]
	public async Task A_connection_holds_at_most_sixteen_video_sessions()
	{
		var (world, _) = await BuiltInAsync();
		using var __ = world;

		for (var i = 0; i < VideoStreamSessionBroker.MaxSessionsPerConnection; i++)
		{
			world.Open(Front, "main");
		}

		var refused = Assert.Throws<VideoStreamBrokerException>(() => world.Open(Front, "main"));
		var other = world.Open(Front, "main", "ui-2");

		Assert.Multiple(() =>
		{
			Assert.That(refused!.Error, Is.EqualTo(VideoStreamError.SessionLimitReached));
			Assert.That(other.SessionId, Is.Not.Empty);
		});
	}

	[Test]
	public async Task An_open_needs_an_enabled_provider_a_known_stream_and_an_accepted_transport()
	{
		var (world, _) = await BuiltInAsync();
		using var __ = world;

		var unknownProvider = Assert.Throws<VideoStreamBrokerException>(() => world.Open(VideoStreamWorld.BuiltIn + "::back", "main"));
		var unknownStream = Assert.Throws<VideoStreamBrokerException>(() => world.Open(Front, "side"));
		var noTransport = Assert.Throws<VideoStreamBrokerException>(() =>
			world.Broker.OpenSession("ui-1", Front, "main", []));

		Assert.Multiple(() =>
		{
			Assert.That(unknownProvider!.Error, Is.EqualTo(VideoStreamError.UnknownProvider));
			Assert.That(unknownStream!.Error, Is.EqualTo(VideoStreamError.UnknownStream));
			Assert.That(noTransport!.Error, Is.EqualTo(VideoStreamError.TransportNotAccepted));
		});
	}

	[Test]
	public async Task A_session_nobody_keeps_alive_expires_after_its_lease_and_the_provider_is_told()
	{
		var time = new ManualTimeProvider();
		var (world, provider) = await BuiltInAsync(time);
		using var _ = world;
		var kept = await OpenActiveAsync(world);
		var forgotten = await OpenActiveAsync(world);

		time.Advance(TimeSpan.FromSeconds(30));
		world.Broker.KeepAliveSession("ui-1", kept);
		time.Advance(TimeSpan.FromSeconds(20));
		await world.WaitForClosedAsync(forgotten);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the provider never heard of the expiry");

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Single(),
				Has.Property(nameof(VideoStreamSessionClosedNotification.SessionId)).EqualTo(forgotten)
					.And.Property(nameof(VideoStreamSessionClosedNotification.Reason))
					.EqualTo(VideoStreamSessionReason.LeaseExpired));
			Assert.That(provider.Closes.Single(), Is.EqualTo((forgotten, VideoStreamSessionReason.LeaseExpired)));
		});
	}

	[Test]
	public async Task When_the_consumer_closes_first_the_providers_own_close_changes_nothing()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var sessionId = await OpenActiveAsync(world);

		world.Broker.CloseSession("ui-1", sessionId);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the provider never closed");
		await world.Context().CloseSessionAsync(sessionId, VideoStreamSessionReason.SourceLost);
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Select(closed => closed.Reason),
				Is.EqualTo(new[] { VideoStreamSessionReason.ConsumerClosed }));
			Assert.That(provider.Closes.Single(), Is.EqualTo((sessionId, VideoStreamSessionReason.ConsumerClosed)));
		});
	}

	[Test]
	public async Task When_the_provider_closes_first_the_consumer_hears_it_once_and_the_provider_is_not_called_back()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var sessionId = await OpenActiveAsync(world);

		await world.Context().CloseSessionAsync(sessionId, VideoStreamSessionReason.SourceLost,
			LocalizedText.FromLiteral("Camera unplugged"));
		world.Broker.CloseSession("ui-1", sessionId);
		await world.WaitForClosedAsync(sessionId);
		await Task.Delay(50);

		var closed = world.Publisher.Of<VideoStreamSessionClosedNotification>();
		Assert.Multiple(() =>
		{
			Assert.That(closed.Select(notification => notification.Reason),
				Is.EqualTo(new[] { VideoStreamSessionReason.SourceLost }));
			Assert.That(closed.Single().Message, Is.EqualTo(LocalizedText.FromLiteral("Camera unplugged")));
			Assert.That(provider.Closes, Is.Empty);
		});
	}

	[Test]
	public async Task Updates_the_provider_sends_while_opening_arrive_in_order_after_the_open()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var release = new TaskCompletionSource();
		var opened = new TaskCompletionSource<string>();
		provider.OnOpen = async (request, _) =>
		{
			opened.SetResult(request.SessionId);
			await release.Task;
			return VideoStreamSessionDescription.Hls("https://camera.local/offer");
		};
		var context = world.Context();

		var ticket = world.Open(Front, "main");
		var sessionId = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Reconnecting,
			reason: VideoStreamSessionReason.SourceLost);
		await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Active,
			VideoStreamSessionDescription.Hls("https://camera.local/renegotiated"),
			VideoStreamSessionReason.SourceRecovered);
		var beforeOpen = world.Publisher.ForSession(sessionId).Count;
		release.SetResult();
		await VideoStreamWorld.WaitForAsync(() => world.Publisher.ForSession(sessionId).Count == 3,
			"the buffered provider traffic never arrived");

		var log = world.Publisher.ForSession(sessionId).Select(notification => notification switch
		{
			VideoStreamSessionChangedNotification changed =>
				$"rev{changed.Revision} {changed.State} {changed.Reason} {changed.Description?.Url.Split('/').Last()}",
			_ => notification.ToString()
		});
		Assert.Multiple(() =>
		{
			Assert.That(ticket.SessionId, Is.EqualTo(sessionId));
			Assert.That(beforeOpen, Is.Zero);
			Assert.That(log, Is.EqualTo(new[]
			{
				"rev1 Active None offer",
				"rev2 Reconnecting SourceLost offer",
				"rev3 Active SourceRecovered renegotiated"
			}));
		});
	}

	[Test]
	public async Task A_provider_that_floods_an_opening_session_fails_it()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var release = new TaskCompletionSource();
		var opened = new TaskCompletionSource<string>();
		provider.OnOpen = async (request, _) =>
		{
			opened.SetResult(request.SessionId);
			await release.Task;
			return VideoStreamSessionDescription.Hls("https://camera.local/main");
		};

		world.Open(Front, "main");
		var sessionId = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
		for (var i = 0; i <= VideoStreamSessionBroker.MaxBufferedProviderOperations; i++)
		{
			await world.Context().UpdateSessionAsync(sessionId, VideoStreamSessionState.Reconnecting);
		}

		release.SetResult();
		await world.WaitForClosedAsync(sessionId);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the provider never closed");

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Single().Reason,
				Is.EqualTo(VideoStreamSessionReason.Failed));
			Assert.That(world.Publisher.Of<VideoStreamSessionChangedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task An_open_that_succeeds_after_the_consumer_closed_is_closed_exactly_once()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var release = new TaskCompletionSource();
		var opened = new TaskCompletionSource<string>();
		provider.OnOpen = async (request, _) =>
		{
			opened.SetResult(request.SessionId);
			await release.Task;
			return VideoStreamSessionDescription.Hls("https://camera.local/main");
		};

		var ticket = world.Open(Front, "main");
		await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
		world.Broker.CloseSession("ui-1", ticket.SessionId);
		release.SetResult();
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the orphaned open was never closed");
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(provider.Closes.Single(), Is.EqualTo((ticket.SessionId, VideoStreamSessionReason.ConsumerClosed)));
			Assert.That(world.Publisher.Of<VideoStreamSessionChangedNotification>(), Is.Empty);
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_plugin_open_answered_after_the_consumer_closed_gets_exactly_one_session_close()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		var release = new TaskCompletionSource();
		world.Plugin.Handler = async (operation, arguments) =>
		{
			if (operation == CapabilityOperations.VideoStreamProvider.SessionOpen)
			{
				await release.Task;
			}

			return world.Plugin.Default(operation, arguments);
		};
		await world.AttachPluginAsync();

		var ticket = world.Open(VideoStreamWorld.PluginId + "::front", "main");
		await VideoStreamWorld.WaitForAsync(
			() => world.Plugin.ArgumentsOf<VideoStreamSessionOpenArguments>(CapabilityOperations.VideoStreamProvider.SessionOpen).Count == 1,
			"the open never reached the plugin");
		world.Broker.CloseSession("ui-1", ticket.SessionId);
		release.SetResult();
		await VideoStreamWorld.WaitForAsync(
			() => world.Plugin.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose).Count == 1,
			"the plugin never got a close");
		await Task.Delay(100);

		var closes = world.Plugin.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose);
		Assert.Multiple(() =>
		{
			Assert.That(closes.Select(close => (close.SessionId, close.ProviderId, close.Reason)),
				Is.EqualTo(new[] { (ticket.SessionId, "front", "ConsumerClosed") }));
			Assert.That(world.Publisher.Of<VideoStreamSessionChangedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task A_built_in_open_that_succeeds_after_the_host_gave_up_is_closed_exactly_once()
	{
		var (world, provider) = await BuiltInAsync(inProcessTimeout: TimeSpan.FromMilliseconds(100));
		using var _ = world;
		provider.OnOpen = async (_, _) =>
		{
			await Task.Delay(400, CancellationToken.None);
			return VideoStreamSessionDescription.Hls("https://camera.local/main");
		};

		var ticket = world.Open(Front, "main");
		await world.WaitForClosedAsync(ticket.SessionId);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the late open was never closed");
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Single().Reason,
				Is.EqualTo(VideoStreamSessionReason.Failed));
			Assert.That(provider.Closes.Single().SessionId, Is.EqualTo(ticket.SessionId));
		});
	}

	[Test]
	public async Task A_hung_open_does_not_hold_up_anything_else()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var never = new TaskCompletionSource<VideoStreamSessionDescription>();
		provider.OnOpen = (request, _) => request.StreamId == "hung"
			? never.Task
			: Task.FromResult(VideoStreamSessionDescription.Hls("https://camera.local/main"));

		var hung = world.Open(Front, "hung");
		var live = await OpenActiveAsync(world);
		world.Broker.KeepAliveSession("ui-1", hung.SessionId);
		world.Broker.CloseSession("ui-1", live);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the live session never closed");

		Assert.Multiple(() =>
		{
			Assert.That(provider.Closes.Single().SessionId, Is.EqualTo(live));
			Assert.That(world.Publisher.ForSession(hung.SessionId), Is.Empty);
		});
	}

	[Test]
	public async Task A_close_the_plugin_rate_limits_is_retried_until_it_is_delivered()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		var refusals = 2;
		world.Plugin.Handler = (operation, arguments) =>
		{
			if (operation == CapabilityOperations.VideoStreamProvider.SessionClose && Interlocked.Decrement(ref refusals) >= 0)
			{
				throw ScriptedVideoPlugin.Error(ProtocolErrorCodes.RateLimited);
			}

			return Task.FromResult(world.Plugin.Default(operation, arguments));
		};
		await world.AttachPluginAsync();
		var sessionId = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::front");

		world.Broker.CloseSession("ui-1", sessionId);
		await VideoStreamWorld.WaitForAsync(
			() => world.Plugin.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose).Count == 3,
			"the close was not retried");
		await Task.Delay(100);

		Assert.That(world.Plugin.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose)
				.Select(close => close.SessionId),
			Is.EqualTo(Enumerable.Repeat(sessionId, 3)));
	}

	[Test]
	public async Task A_close_the_plugin_keeps_rate_limiting_is_given_up_after_a_bounded_number_of_attempts()
	{
		var time = new ManualTimeProvider();
		using var world = new VideoStreamWorld(time: time);
		world.Plugin.AddProvider("front", "r1", "main");
		world.Plugin.Handler = (operation, arguments) =>
			operation == CapabilityOperations.VideoStreamProvider.SessionClose
				? throw ScriptedVideoPlugin.Error(ProtocolErrorCodes.RateLimited)
				: Task.FromResult(world.Plugin.Default(operation, arguments));
		await world.AttachPluginAsync();
		var sessionId = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::front");
		int Closes() => world.Plugin
			.ArgumentsOf<VideoStreamSessionCloseArguments>(CapabilityOperations.VideoStreamProvider.SessionClose).Count;

		world.Broker.CloseSession("ui-1", sessionId);
		await VideoStreamWorld.WaitForAsync(() =>
			{
				time.Advance(TimeSpan.FromSeconds(5));
				return Closes() == VideoStreamSessionBroker.MaxCloseAttempts;
			},
			"the close was not retried up to the bound");
		for (var tick = 0; tick < 12; tick++)
		{
			time.Advance(TimeSpan.FromSeconds(5));
			await Task.Delay(1);
		}

		Assert.That(Closes(), Is.EqualTo(VideoStreamSessionBroker.MaxCloseAttempts));
	}

	[Test]
	public async Task Many_hung_opens_leave_the_plugins_other_invocations_admitted()
	{
		using var world = new VideoStreamWorld(sessions =>
			new PluginCapabilityInvoker(sessions, TimeProvider.System, Serilog.Core.Logger.None));
		var plugin = await WirePlugin.AttachAsync(world);

		for (var i = 0; i < 100; i++)
		{
			world.Open(VideoStreamWorld.PluginId + "::front", "main", $"ui-{i / 16}");
		}

		await VideoStreamWorld.WaitForAsync(() => plugin.Count(CapabilityOperations.VideoStreamProvider.SessionOpen) == 8,
			"the opens never reached the plugin");
		await Task.Delay(100);
		var admitted = await plugin.ActionIsAdmittedAsync();

		Assert.Multiple(() =>
		{
			Assert.That(plugin.Count(CapabilityOperations.VideoStreamProvider.SessionOpen),
				Is.EqualTo(RemoteVideoStreamEndpoint.MaxConcurrentOperations));
			Assert.That(admitted, Is.True);
		});
	}

	[Test]
	public async Task Closing_three_full_connections_at_once_keeps_actions_admitted_and_delivers_every_close()
	{
		using var world = new VideoStreamWorld(sessions =>
			new PluginCapabilityInvoker(sessions, TimeProvider.System, Serilog.Core.Logger.None));
		var plugin = await WirePlugin.AttachAsync(world);
		plugin.AnswerOpens = true;
		var sessions = new List<string>();
		for (var connection = 0; connection < 3; connection++)
		{
			for (var i = 0; i < VideoStreamSessionBroker.MaxSessionsPerConnection; i++)
			{
				sessions.Add(world.Open(VideoStreamWorld.PluginId + "::front", "main", $"ui-{connection}").SessionId);
			}
		}

		await VideoStreamWorld.WaitForAsync(() => world.Publisher.Of<VideoStreamSessionChangedNotification>().Count == 48,
			"not every session opened");
		for (var connection = 0; connection < 3; connection++)
		{
			world.Broker.CloseConnection($"ui-{connection}", VideoStreamSessionReason.ConsumerDisconnected);
		}

		await VideoStreamWorld.WaitForAsync(() => plugin.HeldCloses.Count == RemoteVideoStreamEndpoint.MaxConcurrentCloses,
			"the closes never reached the plugin");
		await Task.Delay(100);
		var inFlight = plugin.HeldCloses.Count;
		var admitted = await plugin.ActionIsAdmittedAsync();
		await plugin.AnswerClosesAsync(48);

		Assert.Multiple(() =>
		{
			Assert.That(inFlight, Is.EqualTo(RemoteVideoStreamEndpoint.MaxConcurrentCloses));
			Assert.That(admitted, Is.True);
			Assert.That(plugin.ClosedSessions, Is.EquivalentTo(sessions));
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Select(closed => closed.Reason).Distinct(),
				Is.EqualTo(new[] { VideoStreamSessionReason.ConsumerDisconnected }));
		});
	}

	[Test]
	public async Task Ending_a_plugin_session_closes_only_the_video_sessions_opened_under_it()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		var first = await world.AttachPluginAsync("com.example.first");
		await world.AttachPluginAsync("com.example.second");
		var ofFirst = await OpenActiveAsync(world, "com.example.first::front");
		var ofSecond = await OpenActiveAsync(world, "com.example.second::front");

		await world.PluginSessions.Terminate(first, 4000, "gone");
		await world.WaitForClosedAsync(ofFirst);
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Single(),
				Has.Property(nameof(VideoStreamSessionClosedNotification.SessionId)).EqualTo(ofFirst)
					.And.Property(nameof(VideoStreamSessionClosedNotification.Reason))
					.EqualTo(VideoStreamSessionReason.ProviderRemoved));
			Assert.That(world.Publisher.ForSession(ofSecond), Has.Count.EqualTo(1));
			Assert.That(world.Registry.GetProviders().Select(entry => entry.QualifiedId),
				Is.EqualTo(new[] { "com.example.second::front" }));
		});
	}

	[Test]
	public async Task A_late_prune_of_a_replaced_session_leaves_the_replacements_providers_alone()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		await world.AttachPluginAsync();
		var underOld = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::front");

		var (replacement, connection) = await world.ConnectPluginAsync();
		await world.WaitForClosedAsync(underOld);
		var listedBeforeAttach = world.Registry.GetProviders().Select(entry => entry.QualifiedId).ToList();
		await world.Registry.AttachRemoteAsync(VideoStreamWorld.PluginId, replacement, connection);
		var underNew = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::front");

		Assert.Multiple(() =>
		{
			Assert.That(listedBeforeAttach, Is.EqualTo(new[] { VideoStreamWorld.PluginId + "::front" }));
			Assert.That(world.Publisher.ForSession(underNew), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_describe_answered_after_its_connection_ended_is_not_applied()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		var release = new TaskCompletionSource();
		world.Plugin.Handler = async (operation, arguments) =>
		{
			await release.Task;
			return world.Plugin.Default(operation, arguments);
		};
		var (sessionId, connection) = await world.ConnectPluginAsync();

		var pull = world.Registry.AttachRemoteAsync(VideoStreamWorld.PluginId, sessionId, connection);
		world.PluginSessions.Detach(sessionId, DateTimeOffset.UtcNow);
		release.SetResult();
		await pull;

		Assert.That(world.Registry.GetProviders(), Is.Empty);
	}

	[Test]
	public async Task A_changed_registration_closes_only_the_sessions_opened_on_the_old_one()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		world.Plugin.AddProvider("back", "b1", "main");
		var (pluginSession, connection) = await world.ConnectPluginAsync();
		await world.Registry.AttachRemoteAsync(VideoStreamWorld.PluginId, pluginSession, connection);
		var onFront = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::front");
		var onBack = await OpenActiveAsync(world, VideoStreamWorld.PluginId + "::back");

		world.Plugin.AddProvider("front", "r2", "main", "side");
		await world.Registry.RequestProvidersPullAsync(VideoStreamWorld.PluginId, pluginSession, connection);
		await world.WaitForClosedAsync(onFront);

		Assert.Multiple(() =>
		{
			Assert.That(world.Publisher.Of<VideoStreamSessionClosedNotification>().Single(),
				Has.Property(nameof(VideoStreamSessionClosedNotification.SessionId)).EqualTo(onFront)
					.And.Property(nameof(VideoStreamSessionClosedNotification.Reason))
					.EqualTo(VideoStreamSessionReason.ProviderRemoved));
			Assert.That(world.Publisher.ForSession(onBack), Has.Count.EqualTo(1));
			Assert.That(world.Registry.GetProviders().Single(entry => entry.ProviderId == "front").Streams.Select(stream => stream.Id),
				Is.EqualTo(new[] { "main", "side" }));
		});
	}

	[Test]
	public async Task Disabling_an_integration_closes_its_sessions_and_hides_it_until_it_is_enabled_again()
	{
		var (world, provider) = await BuiltInAsync();
		using var _ = world;
		var handler = new VideoStreamIntegrationStateChangedHandler(world.Integrations, world.Registry, world.Broker);
		var sessionId = await OpenActiveAsync(world);

		world.Integrations.SetEnabled(VideoStreamWorld.BuiltIn, false);
		await handler.Handle(new IntegrationStateChangedNotification(VideoStreamWorld.BuiltIn), CancellationToken.None);
		await VideoStreamWorld.WaitForAsync(() => provider.Closes.Count == 1, "the provider never closed");
		var listedWhileDisabled = world.Registry.GetProviders().Count;
		var refused = Assert.Throws<VideoStreamBrokerException>(() => world.Open(Front, "main"));

		world.Integrations.SetEnabled(VideoStreamWorld.BuiltIn, true);
		await handler.Handle(new IntegrationStateChangedNotification(VideoStreamWorld.BuiltIn), CancellationToken.None);
		var reopened = await OpenActiveAsync(world);

		Assert.Multiple(() =>
		{
			Assert.That(provider.Closes.Single(), Is.EqualTo((sessionId, VideoStreamSessionReason.ProviderRemoved)));
			Assert.That(listedWhileDisabled, Is.Zero);
			Assert.That(refused!.Error, Is.EqualTo(VideoStreamError.UnknownProvider));
			Assert.That(reopened, Is.Not.EqualTo(sessionId));
			Assert.That(world.Publisher.Of<VideoStreamCatalogChangedNotification>(), Has.Count.GreaterThanOrEqualTo(3));
		});
	}

	[Test]
	public async Task A_provider_can_only_report_on_sessions_of_its_own()
	{
		using var world = new VideoStreamWorld();
		world.Plugin.AddProvider("front", "r1", "main");
		await world.AttachPluginAsync("com.example.first");
		var sessionId = await OpenActiveAsync(world, "com.example.first::front");

		var update = Assert.Throws<VideoStreamBrokerException>(() => world.Broker.ApplyProviderUpdate("com.example.other",
			sessionId,
			VideoStreamSessionState.Suspended,
			null,
			VideoStreamSessionReason.None,
			null));
		var closed = world.Broker.ApplyProviderClose("com.example.other", sessionId, VideoStreamSessionReason.ProviderClosed, null);

		Assert.Multiple(() =>
		{
			Assert.That(update!.Error, Is.EqualTo(VideoStreamError.UnknownSession));
			Assert.That(closed, Is.False);
			Assert.That(world.Publisher.ForSession(sessionId), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_provider_listing_too_many_streams_is_cut_to_the_limit()
	{
		using var world = new VideoStreamWorld();
		var provider = new ScriptedVideoProvider("front");
		provider.Streams.AddRange(Enumerable.Range(0, 300)
			.Select(i => new VideoStreamDescriptor($"s{i}", LocalizedText.FromLiteral($"Stream {i}"))));
		provider.Streams.Add(new VideoStreamDescriptor("s0", LocalizedText.FromLiteral("Duplicate")));

		await world.Context().RegisterProviderAsync(provider);

		var streams = world.Registry.GetProviders().Single().Streams;
		Assert.That(streams.Select(stream => stream.Id),
			Is.EqualTo(Enumerable.Range(0, 256).Select(i => $"s{i}")));
	}

	private sealed class WirePlugin
	{
		private readonly ConcurrentQueue<CapabilityInvokePayload> _received = new();
		private readonly PluginCapabilityInvoker _invoker;

		private WirePlugin(PluginCapabilityInvoker invoker) => _invoker = invoker;

		public bool AnswerOpens { get; set; }

		public ConcurrentQueue<ProtocolEnvelope> HeldCloses { get; } = new();

		public ConcurrentBag<string> ClosedSessions { get; } = [];

		public static async Task<WirePlugin> AttachAsync(VideoStreamWorld world)
		{
			var plugin = new WirePlugin((PluginCapabilityInvoker)world.Invoker);
			var (sessionId, connection) = await world.ConnectPluginAsync();
			connection.OnSend = plugin.OnSendAsync;
			await world.Registry.AttachRemoteAsync(VideoStreamWorld.PluginId, sessionId, connection);
			return plugin;
		}

		public int Count(string operation) => _received.Count(payload => payload.Operation == operation);

		public async Task<bool> ActionIsAdmittedAsync()
		{
			using var cancellation = new CancellationTokenSource();
			var action = _invoker.InvokeAsync(VideoStreamWorld.PluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.Actions, LocalId = "record", Operation = CapabilityOperations.Actions.Execute
				},
				cancellation.Token);
			var sent = false;
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
			while (!sent && !action.IsCompleted && DateTime.UtcNow < deadline)
			{
				sent = _received.Any(payload => payload.Kind == CapabilityKinds.Actions);
				await Task.Delay(5);
			}

			await cancellation.CancelAsync();
			try
			{
				await action;
			}
			catch (OperationCanceledException)
			{
			}

			return sent;
		}

		public async Task AnswerClosesAsync(int expected)
		{
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
			while (ClosedSessions.Count < expected && DateTime.UtcNow < deadline)
			{
				if (HeldCloses.TryDequeue(out var close))
				{
					var arguments = close.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)!
						.Arguments!.Value.Deserialize<VideoStreamSessionCloseArguments>(PluginProtocolJson.Options)!;
					ClosedSessions.Add(arguments.SessionId);
					Reply(close, null);
					continue;
				}

				await Task.Delay(2);
			}
		}

		private Task OnSendAsync(ProtocolEnvelope envelope)
		{
			if (envelope.Type != MessageTypes.CapabilityInvoke)
			{
				return Task.CompletedTask;
			}

			var payload = envelope.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)!;
			_received.Enqueue(payload);
			switch (payload.Operation)
			{
				case CapabilityOperations.VideoStreamProvider.Describe:
					Reply(envelope, new VideoStreamProviderDescribePayload
					{
						Providers =
						[
							new VideoStreamProviderDto
							{
								Id = "front", Name = LocalizedText.FromLiteral("Front"), RegistrationId = "r1"
							}
						]
					});
					break;
				case CapabilityOperations.VideoStreamProvider.Streams:
					Reply(envelope, new VideoStreamProviderStreamsResult
					{
						Streams = [new VideoStreamDescriptorDto { Id = "main", Name = LocalizedText.FromLiteral("Main") }]
					});
					break;
				case CapabilityOperations.VideoStreamProvider.SessionOpen when AnswerOpens:
					Reply(envelope, new VideoStreamSessionOpenResult
					{
						Description = new VideoStreamSessionDescriptionDto { Transport = "hls", Url = "https://camera.local/live" },
						RegistrationId = "r1"
					});
					break;
				case CapabilityOperations.VideoStreamProvider.SessionClose:
					HeldCloses.Enqueue(envelope);
					break;
			}

			return Task.CompletedTask;
		}

		private void Reply(ProtocolEnvelope request, object? data)
			=> _ = Task.Run(() => _invoker.TryComplete(VideoStreamWorld.PluginId,
				new ProtocolEnvelope
				{
					Type = MessageTypes.CapabilityResult,
					Id = Guid.NewGuid().ToString(),
					CorrelationId = request.Id,
					Payload = JsonSerializer.SerializeToElement(
						new CapabilityResultPayload
						{
							Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
						},
						PluginProtocolJson.Options)
				}));
	}
}
