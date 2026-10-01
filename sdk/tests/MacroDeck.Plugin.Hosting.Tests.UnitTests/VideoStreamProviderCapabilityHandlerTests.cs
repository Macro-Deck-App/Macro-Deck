using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.VideoStreams;
using System.Text.Json;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class VideoStreamProviderCapabilityHandlerTests
{
	private VideoStreamFixture _fixture = null!;
	private TestVideoProvider _provider = null!;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new VideoStreamFixture();
		_fixture.State.RaiseConnected(resumed: false);
		_provider = new TestVideoProvider();
		await _fixture.Context.RegisterProviderAsync(_provider);
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public void The_capability_is_declared_only_when_an_integration_implements_video_streams()
	{
		using var without = new VideoStreamFixture(new TestIntegration());

		var declared = _fixture.Handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(without.Handler.DeclareCapabilities(), Is.Empty);
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.VideoStreamProvider));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That((declared[0].VersionRange.Minimum, declared[0].VersionRange.Maximum), Is.EqualTo((1, 1)));
		});
	}

	[Test]
	public async Task Registering_tells_the_host_and_describe_lists_a_fresh_registration_per_registration()
	{
		var first = Describe(await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe));
		await _fixture.Context.UnregisterProviderAsync("cam");
		var registration = await _fixture.Context.RegisterProviderAsync(new TestVideoProvider());
		var second = Describe(await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe));

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Invoker.Calls[0].Operation, Is.EqualTo(HostOperations.VideoStreams.ProvidersChanged));
			Assert.That(registration, Is.EqualTo(new VideoStreamProviderRegistration("com.example.test::cam", "cam")));
			Assert.That(first.Providers.Single().Id, Is.EqualTo("cam"));
			Assert.That(Guid.TryParse(first.Providers[0].RegistrationId, out _), Is.True);
			Assert.That(second.Providers.Single().RegistrationId, Is.Not.EqualTo(first.Providers[0].RegistrationId));
		});
	}

	[Test]
	public void A_provider_id_registered_by_another_integration_is_rejected()
	{
		var other = _fixture.Registry.ContextFor(new object());

		Assert.That(() => other.RegisterProviderAsync(new TestVideoProvider()), Throws.ArgumentException);
	}

	[Test]
	public async Task A_seventeenth_provider_is_rejected()
	{
		for (var index = 1; index < 16; index++)
		{
			await _fixture.Context.RegisterProviderAsync(new TestVideoProvider($"cam-{index}"));
		}

		Assert.That(() => _fixture.Context.RegisterProviderAsync(new TestVideoProvider("one-too-many")),
			Throws.ArgumentException);
	}

	[Test]
	public async Task Streams_of_an_unknown_provider_are_refused_with_the_unknown_provider_reason()
	{
		var result = await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Streams,
			new VideoStreamProviderStreamsArguments { ProviderId = "nobody" });

		Assert.That(VideoStreamFixture.Reason(result), Is.EqualTo(ProtocolErrorReasons.VideoStreamUnknownProvider));
	}

	[Test]
	public async Task Streams_keep_only_the_first_256_valid_streams()
	{
		_provider.Streams =
		[
			new VideoStreamDescriptor("bad\nid", LocalizedText.FromLiteral("Broken")),
			.. Enumerable.Range(0, 300)
				.Select(index => new VideoStreamDescriptor($"s{index}", LocalizedText.FromLiteral($"Stream {index}"),
					State: VideoStreamState.Connecting))
		];

		var result = await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Streams,
			new VideoStreamProviderStreamsArguments { ProviderId = "cam" });
		var streams = result.Data!.Value.Deserialize<VideoStreamProviderStreamsResult>(PluginProtocolJson.Options)!
			.Streams;

		Assert.Multiple(() =>
		{
			Assert.That(streams, Has.Count.EqualTo(256));
			Assert.That(streams[0].Id, Is.EqualTo("s0"));
			Assert.That(streams[0].State, Is.EqualTo("Connecting"));
		});
	}

	[Test]
	public async Task An_open_returns_the_description_and_the_registration_it_was_opened_on()
	{
		VideoStreamOpenRequest? request = null;
		_provider.Open = (received, _) =>
		{
			request = received;
			return Task.FromResult(VideoStreamSessionDescription.Mjpeg("http://127.0.0.1:8080/cam.mjpg"));
		};
		var registrationId = Describe(await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe))
			.Providers[0].RegistrationId;

		var result = await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionOpen,
			new VideoStreamSessionOpenArguments
			{
				SessionId = "s1",
				ProviderId = "cam",
				StreamId = "main",
				AcceptedTransports = ["mjpeg", "hls"]
			});
		var opened = result.Data!.Value.Deserialize<VideoStreamSessionOpenResult>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(opened.Description.Transport, Is.EqualTo("mjpeg"));
			Assert.That(opened.Description.Url, Is.EqualTo("http://127.0.0.1:8080/cam.mjpg"));
			Assert.That(opened.RegistrationId, Is.EqualTo(registrationId));
			Assert.That(request!.AcceptedTransports, Is.EqualTo(new[] { "mjpeg", "hls" }));
		});
	}

	[Test]
	public async Task An_open_from_an_older_host_that_still_sends_a_consumer_is_served_and_the_consumer_ignored()
	{
		var result = await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionOpen,
			new
			{
				sessionId = "s1",
				providerId = "cam",
				streamId = "main",
				acceptedTransports = new[] { "hls" },
				consumer = new { deviceId = "phone", hostAddress = "http://10.0.0.2:8191/", connectionKind = "UsbTunnel" }
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(_provider.OpenCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_description_the_host_could_not_use_is_refused_on_the_provider_side_and_closed_once()
	{
		_provider.Open = (_, _) => Task.FromResult<VideoStreamSessionDescription>(null!);

		var result = await _fixture.OpenAsync("s1");

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.True);
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.Failed) }));
		});
	}

	[Test]
	public async Task A_resume_returns_the_new_description_or_none_when_the_previous_one_is_still_valid()
	{
		await _fixture.OpenAsync("s1");

		var unchanged = await _fixture.ResumeAsync("s1");
		_provider.Resume = _ => Task.FromResult<VideoStreamSessionDescription?>(
			VideoStreamSessionDescription.Hls("http://127.0.0.1:9000/new.m3u8"));
		var changed = await _fixture.ResumeAsync("s1");

		Assert.Multiple(() =>
		{
			Assert.That(Resumed(unchanged).Description, Is.Null);
			Assert.That(Resumed(changed).Description!.Url, Is.EqualTo("http://127.0.0.1:9000/new.m3u8"));
		});
	}

	[Test]
	public async Task A_description_in_a_transport_the_consumer_does_not_accept_is_refused_and_closed_once()
	{
		_provider.Open = (_, _) => Task.FromResult(VideoStreamSessionDescription.FromUrl("rtsp", "http://camera.local/x"));

		var result = await _fixture.OpenAsync("s1", "cam", "hls");

		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamFixture.Reason(result),
				Is.EqualTo(ProtocolErrorReasons.VideoStreamTransportNotAccepted));
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.Failed) }));
		});
	}

	[Test]
	public async Task A_close_during_the_open_is_carried_out_exactly_once_after_the_open_returns()
	{
		var release = new TaskCompletionSource<VideoStreamSessionDescription>();
		_provider.Open = (_, _) => release.Task;

		var open = _fixture.OpenAsync("s1");
		await VideoStreamFixture.WaitForAsync(() => _provider.OpenCount == 1);
		var close = await _fixture.CloseAsync("s1");
		var repeated = await _fixture.CloseAsync("s1", reason: "LeaseExpired");
		var closesWhileOpening = _provider.Closes.Count;

		release.SetResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));
		var opened = await open;

		Assert.Multiple(() =>
		{
			Assert.That(close.IsFailure, Is.False);
			Assert.That(repeated.IsFailure, Is.False);
			Assert.That(closesWhileOpening, Is.Zero);
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.ConsumerClosed) }));
			Assert.That(VideoStreamFixture.Reason(opened), Is.EqualTo(ProtocolErrorReasons.VideoStreamUnknownSession));
		});
	}

	[Test]
	public async Task An_open_session_is_closed_exactly_once()
	{
		await _fixture.OpenAsync("s1");

		await _fixture.CloseAsync("s1");
		await _fixture.CloseAsync("s1");

		Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.ConsumerClosed) }));
	}

	[Test]
	public async Task An_open_that_arrives_after_its_close_is_refused_without_reaching_the_provider()
	{
		await _fixture.CloseAsync("s1");

		var late = await _fixture.OpenAsync("s1");

		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamFixture.Reason(late), Is.EqualTo(ProtocolErrorReasons.VideoStreamUnknownSession));
			Assert.That(_provider.OpenCount, Is.Zero);
			Assert.That(_provider.Closes, Is.Empty);
		});
	}

	[Test]
	public async Task Unregistering_closes_the_providers_sessions_then_tells_the_host()
	{
		await _fixture.OpenAsync("s1");
		await _fixture.OpenAsync("s2");
		var before = _fixture.Invoker.Calls.Count;

		await _fixture.Context.UnregisterProviderAsync("cam");

		var sent = _fixture.Invoker.Calls.Skip(before).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes.Select(close => close.Reason),
				Is.EqualTo(new[] { VideoStreamSessionReason.ProviderRemoved, VideoStreamSessionReason.ProviderRemoved }));
			Assert.That(_provider.Closes.Select(close => close.SessionId), Is.EquivalentTo(new[] { "s1", "s2" }));
			Assert.That(sent.Select(call => call.Operation),
				Is.EqualTo(new[]
				{
					HostOperations.VideoStreams.SessionClose, HostOperations.VideoStreams.SessionClose,
					HostOperations.VideoStreams.ProvidersChanged
				}));
			Assert.That(sent.Take(2).Select(call => ((VideoStreamsSessionCloseArguments)call.Arguments!).Reason),
				Is.All.EqualTo("ProviderRemoved"));
		});
	}

	[Test]
	public async Task Unregistering_while_a_session_opens_closes_it_once_the_open_returns()
	{
		var release = new TaskCompletionSource<VideoStreamSessionDescription>();
		_provider.Open = (_, _) => release.Task;
		var open = _fixture.OpenAsync("s1");
		await VideoStreamFixture.WaitForAsync(() => _provider.OpenCount == 1);

		await _fixture.Context.UnregisterProviderAsync("cam");
		var closesBeforeReturn = _provider.Closes.Count;
		release.SetResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));
		var opened = await open;

		Assert.Multiple(() =>
		{
			Assert.That(closesBeforeReturn, Is.Zero);
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.ProviderRemoved) }));
			Assert.That(opened.IsFailure, Is.True);
		});
	}

	[Test]
	public async Task A_close_racing_an_unregister_closes_the_original_provider_once_and_never_its_replacement()
	{
		for (var round = 0; round < 200; round++)
		{
			using var fixture = new VideoStreamFixture();
			fixture.State.RaiseConnected(resumed: false);
			var original = new TestVideoProvider();
			var replacement = new TestVideoProvider();
			await fixture.Context.RegisterProviderAsync(original);
			await fixture.OpenAsync("s1");

			using var start = new Barrier(2);
			var close = Task.Run(async () =>
			{
				start.SignalAndWait();
				await fixture.CloseAsync("s1");
			});
			var replace = Task.Run(async () =>
			{
				start.SignalAndWait();
				await fixture.Context.UnregisterProviderAsync("cam");
				await fixture.Context.RegisterProviderAsync(replacement);
			});
			await Task.WhenAll(close, replace);

			Assert.Multiple(() =>
			{
				Assert.That(original.Closes.Select(closed => closed.SessionId), Is.EqualTo(new[] { "s1" }),
					$"round {round}");
				Assert.That(replacement.Closes, Is.Empty, $"round {round}");
			});
		}
	}

	[Test]
	public async Task A_reconnect_sweep_closes_the_provider_the_session_was_opened_on_even_after_it_was_replaced()
	{
		await _fixture.OpenAsync("s1");
		var replacement = new TestVideoProvider();
		_fixture.State.Connected += (_, _) =>
		{
			_fixture.Context.UnregisterProviderAsync("cam").GetAwaiter().GetResult();
			_fixture.Context.RegisterProviderAsync(replacement).GetAwaiter().GetResult();
		};

		_fixture.State.RaiseConnected(resumed: true);
		await VideoStreamFixture.WaitForAsync(() => _provider.Closes.Count == 1);
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.HostDisconnected) }));
			Assert.That(replacement.Closes, Is.Empty);
		});
	}

	[Test]
	public async Task A_resumed_connection_closes_the_previous_connections_sessions_but_not_a_fresh_open()
	{
		await _fixture.OpenAsync("before");

		_fixture.State.RaiseConnected(resumed: true);
		await _fixture.OpenAsync("after");
		await VideoStreamFixture.WaitForAsync(() => _provider.Closes.Count == 1);
		await Task.Delay(50);
		var suspended = await _fixture.SuspendAsync("after");

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("before", VideoStreamSessionReason.HostDisconnected) }));
			Assert.That(suspended.IsFailure, Is.False);
		});
	}

	[Test]
	public async Task A_session_opening_across_a_reconnect_is_closed_once_its_open_returns()
	{
		var release = new TaskCompletionSource<VideoStreamSessionDescription>();
		_provider.Open = (_, _) => release.Task;
		var open = _fixture.OpenAsync("s1");
		await VideoStreamFixture.WaitForAsync(() => _provider.OpenCount == 1);

		_fixture.State.RaiseConnected(resumed: true);
		release.SetResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));
		await open;

		Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.HostDisconnected) }));
	}

	[Test]
	public async Task The_disconnect_sweep_does_not_wait_for_a_provider_and_spares_the_next_connection()
	{
		var hang = new TaskCompletionSource();
		_provider.OnClose = (_, _) => hang.Task;
		await _fixture.OpenAsync("s1");

		var sweep = Task.Run(() => _fixture.State.RaiseConnectionEnded());
		await sweep.WaitAsync(TimeSpan.FromSeconds(1));
		_fixture.State.RaiseConnected(resumed: false);
		var reopened = await _fixture.OpenAsync("s2").WaitAsync(TimeSpan.FromSeconds(1));
		await VideoStreamFixture.WaitForAsync(() => _provider.Closes.Count == 1);
		hang.SetResult();
		var suspended = await _fixture.SuspendAsync("s2");

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.HostDisconnected) }));
			Assert.That(reopened.IsFailure, Is.False);
			Assert.That(suspended.IsFailure, Is.False);
		});
	}

	[Test]
	public async Task The_disconnect_sweep_leaves_the_provider_registered()
	{
		_fixture.State.RaiseConnectionEnded();
		_fixture.State.RaiseConnected(resumed: false);

		var described = Describe(await _fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe));

		Assert.That(described.Providers.Select(provider => provider.Id), Is.EqualTo(new[] { "cam" }));
	}

	[TestCase(VideoStreamErrorCode.UnknownProvider, ProtocolErrorReasons.VideoStreamUnknownProvider, false)]
	[TestCase(VideoStreamErrorCode.UnknownStream, ProtocolErrorReasons.VideoStreamUnknownStream, false)]
	[TestCase(VideoStreamErrorCode.StreamUnavailable, ProtocolErrorReasons.VideoStreamStreamUnavailable, true)]
	[TestCase(VideoStreamErrorCode.CapacityReached, ProtocolErrorReasons.VideoStreamCapacityReached, true)]
	[TestCase(VideoStreamErrorCode.Busy, ProtocolErrorReasons.VideoStreamBusy, true)]
	[TestCase(VideoStreamErrorCode.TransportNotAccepted, ProtocolErrorReasons.VideoStreamTransportNotAccepted, false)]
	public async Task A_provider_rejection_reaches_the_host_as_its_reason_and_needs_no_close(
		VideoStreamErrorCode code,
		string reason,
		bool retryable)
	{
		_provider.Open = (_, _) => throw new VideoStreamException(code, "Rejected.");

		var result = await _fixture.OpenAsync("s1");

		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamFixture.Reason(result), Is.EqualTo(reason));
			Assert.That(result.Error!.Retryable, Is.EqualTo(retryable));
			Assert.That(_provider.Closes, Is.Empty);
		});
	}

	[Test]
	public void Every_error_code_survives_the_round_trip_over_the_wire()
	{
		foreach (var code in Enum.GetValues<VideoStreamErrorCode>())
		{
			var error = VideoStreamWire.ToError(code, "message");

			Assert.That(VideoStreamWire.FromError(error.Code, error.Details?.GetValueOrDefault("reason")),
				Is.EqualTo(code),
				code.ToString());
		}
	}

	[Test]
	public void A_host_rejection_reaches_the_provider_as_a_video_stream_exception()
	{
		_fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.StreamsChanged
			? HostInvocationException.From(new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnavailable,
				Message = "No such provider.",
				Retryable = false,
				Details = new Dictionary<string, string> { ["reason"] = ProtocolErrorReasons.VideoStreamUnknownProvider }
			})
			: null;

		var thrown = Assert.ThrowsAsync<VideoStreamException>(() => _fixture.Context.NotifyStreamsChangedAsync("cam"));

		Assert.That(thrown!.ErrorCode, Is.EqualTo(VideoStreamErrorCode.UnknownProvider));
	}

	[Test]
	public async Task A_rate_limited_update_is_retried_and_the_sessions_later_update_waits_for_it()
	{
		await _fixture.OpenAsync("s1");
		var refusals = 2;
		_fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.SessionUpdate &&
			((VideoStreamsSessionUpdateArguments)call.Arguments!).State == nameof(VideoStreamSessionState.Reconnecting) &&
			Interlocked.Decrement(ref refusals) >= 0
				? RateLimited()
				: null;

		var first = _fixture.Context.UpdateSessionAsync("s1", VideoStreamSessionState.Reconnecting);
		var second = _fixture.Context.UpdateSessionAsync("s1", VideoStreamSessionState.Active);
		await Task.WhenAll(first, second);

		Assert.That(UpdateStates(), Is.EqualTo(new[] { "Reconnecting", "Reconnecting", "Reconnecting", "Active" }));
	}

	[Test]
	public async Task A_session_update_the_host_keeps_rate_limiting_fails_as_busy_after_a_bounded_number_of_attempts()
	{
		await _fixture.OpenAsync("s1");
		_fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.SessionUpdate ? RateLimited() : null;

		var thrown = Assert.ThrowsAsync<VideoStreamException>(() =>
			_fixture.Context.UpdateSessionAsync("s1", VideoStreamSessionState.Reconnecting));

		Assert.Multiple(() =>
		{
			Assert.That(thrown!.ErrorCode, Is.EqualTo(VideoStreamErrorCode.Busy));
			Assert.That(_fixture.Invoker.Calls.Count(call => call.Operation == HostOperations.VideoStreams.SessionUpdate),
				Is.EqualTo(5));
		});
	}

	[Test]
	public async Task A_provider_close_the_host_rate_limits_twice_reaches_the_host_exactly_once()
	{
		var time = new ManualTimeProvider();
		using var fixture = await ConnectedFixtureAsync(time);
		await fixture.OpenAsync("s1");
		var refusals = 2;
		var accepted = new List<string>();
		fixture.Invoker.Fail = call =>
		{
			if (call.Operation == HostOperations.VideoStreams.SessionClose && Interlocked.Decrement(ref refusals) >= 0)
			{
				return RateLimited();
			}

			lock (accepted)
			{
				accepted.Add(call.Operation);
			}

			return null;
		};

		await DriveAsync(time, fixture.Context.CloseSessionAsync("s1", VideoStreamSessionReason.SourceLost));
		await fixture.Context.CloseSessionAsync("s1");

		Assert.That(accepted, Is.EqualTo(new[] { HostOperations.VideoStreams.SessionClose }));
	}

	[Test]
	public async Task An_unregister_the_host_briefly_rate_limits_still_delivers_every_close_and_the_change()
	{
		var time = new ManualTimeProvider();
		using var fixture = await ConnectedFixtureAsync(time);
		await fixture.OpenAsync("s1");
		await fixture.OpenAsync("s2");
		var refusals = 2;
		var accepted = new List<string>();
		fixture.Invoker.Fail = call =>
		{
			if (Interlocked.Decrement(ref refusals) >= 0)
			{
				return RateLimited();
			}

			lock (accepted)
			{
				accepted.Add(call.Operation);
			}

			return null;
		};

		await DriveAsync(time, fixture.Context.UnregisterProviderAsync("cam"));

		Assert.That(accepted,
			Is.EqualTo(new[]
			{
				HostOperations.VideoStreams.SessionClose, HostOperations.VideoStreams.SessionClose,
				HostOperations.VideoStreams.ProvidersChanged
			}));
	}

	[Test]
	public async Task A_provider_close_the_host_keeps_rate_limiting_fails_as_busy_and_can_be_retried()
	{
		var time = new ManualTimeProvider();
		using var fixture = await ConnectedFixtureAsync(time);
		await fixture.OpenAsync("s1");
		fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.SessionClose ? RateLimited() : null;

		var busy = await CaptureAsync(time, fixture.Context.CloseSessionAsync("s1"));
		var attempts = Count(fixture, HostOperations.VideoStreams.SessionClose);
		fixture.Invoker.Fail = _ => null;
		await fixture.Context.CloseSessionAsync("s1");

		Assert.Multiple(() =>
		{
			Assert.That(busy?.ErrorCode, Is.EqualTo(VideoStreamErrorCode.Busy));
			Assert.That(attempts, Is.EqualTo(5));
			Assert.That(Count(fixture, HostOperations.VideoStreams.SessionClose), Is.EqualTo(6));
		});
	}

	[Test]
	public async Task A_streams_changed_notice_the_host_keeps_rate_limiting_fails_as_busy_after_a_bounded_number_of_attempts()
	{
		var time = new ManualTimeProvider();
		using var fixture = await ConnectedFixtureAsync(time);
		fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.StreamsChanged ? RateLimited() : null;

		var busy = await CaptureAsync(time, fixture.Context.NotifyStreamsChangedAsync("cam"));

		Assert.Multiple(() =>
		{
			Assert.That(busy?.ErrorCode, Is.EqualTo(VideoStreamErrorCode.Busy));
			Assert.That(Count(fixture, HostOperations.VideoStreams.StreamsChanged), Is.EqualTo(5));
		});
	}

	[Test]
	public async Task A_registration_the_host_keeps_rate_limiting_fails_as_busy_and_registers_nothing()
	{
		var time = new ManualTimeProvider();
		using var fixture = new VideoStreamFixture(time: time);
		fixture.State.RaiseConnected(resumed: false);
		fixture.Invoker.Fail = call => call.Operation == HostOperations.VideoStreams.ProvidersChanged ? RateLimited() : null;

		var busy = await CaptureAsync(time, fixture.Context.RegisterProviderAsync(new TestVideoProvider()));
		var described = Describe(await fixture.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe));

		Assert.Multiple(() =>
		{
			Assert.That(busy?.ErrorCode, Is.EqualTo(VideoStreamErrorCode.Busy));
			Assert.That(described.Providers, Is.Empty);
		});
	}

	[Test]
	public async Task Against_a_host_without_video_streams_nothing_is_registered_and_nothing_throws()
	{
		using var old = new VideoStreamFixture();
		old.Invoker.Fail = VideoRecordingInvoker.Unsupported;

		var registration = await old.Context.RegisterProviderAsync(new TestVideoProvider());
		var described = Describe(await old.InvokeAsync(CapabilityOperations.VideoStreamProvider.Describe));

		Assert.Multiple(() =>
		{
			Assert.That(registration.QualifiedId, Is.Empty);
			Assert.That(registration.ProviderId, Is.Empty);
			Assert.That(described.Providers, Is.Empty);
			Assert.DoesNotThrowAsync(() => old.Context.NotifyStreamsChangedAsync("cam"));
			Assert.DoesNotThrowAsync(() => old.Context.UnregisterProviderAsync("cam"));
		});
	}

	[Test]
	public async Task The_provider_closing_a_session_itself_gets_no_close_call()
	{
		await _fixture.OpenAsync("s1");

		await _fixture.Context.CloseSessionAsync("s1", VideoStreamSessionReason.SourceLost);
		await _fixture.CloseAsync("s1");
		await _fixture.Context.CloseSessionAsync("s1");

		var closes = _fixture.Invoker.Calls.Where(call => call.Operation == HostOperations.VideoStreams.SessionClose)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes, Is.Empty);
			Assert.That(closes, Has.Count.EqualTo(1));
			Assert.That(((VideoStreamsSessionCloseArguments)closes[0].Arguments!).Reason, Is.EqualTo("SourceLost"));
		});
	}

	[Test]
	public async Task Session_calls_for_an_unknown_session_are_refused()
	{
		var result = await _fixture.SuspendAsync("never-opened");

		Assert.That(VideoStreamFixture.Reason(result), Is.EqualTo(ProtocolErrorReasons.VideoStreamUnknownSession));
	}

	[Test]
	public void Unknown_wire_values_fall_back_to_the_documented_members()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VideoStreamWire.ParseState("Exploded"), Is.EqualTo(VideoStreamState.Unavailable));
			Assert.That(VideoStreamWire.ParseSessionState("Exploded"), Is.EqualTo(VideoStreamSessionState.Reconnecting));
			Assert.That(VideoStreamWire.ParseReason("Exploded"), Is.EqualTo(VideoStreamSessionReason.None));
			Assert.That(VideoStreamWire.ParseReason("3"), Is.EqualTo(VideoStreamSessionReason.None));
			Assert.That(VideoStreamWire.FromError(ProtocolErrorCodes.CapabilityUnavailable, "video_stream_from_the_future"),
				Is.EqualTo(VideoStreamErrorCode.Failed));
		});
	}

	[Test]
	public async Task An_unknown_close_reason_reaches_the_provider_as_none()
	{
		await _fixture.OpenAsync("s1");

		await _fixture.CloseAsync("s1", reason: "ConsumerFellAsleep");

		Assert.That(_provider.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.None) }));
	}

	private static async Task<VideoStreamFixture> ConnectedFixtureAsync(TimeProvider time)
	{
		var fixture = new VideoStreamFixture(time: time);
		fixture.State.RaiseConnected(resumed: false);
		await fixture.Context.RegisterProviderAsync(new TestVideoProvider());
		return fixture;
	}

	private static async Task DriveAsync(ManualTimeProvider time, Task pending)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!pending.IsCompleted && DateTime.UtcNow < deadline)
		{
			time.Advance(TimeSpan.FromSeconds(1));
			await Task.Delay(1);
		}

		await pending;
	}

	private static async Task<VideoStreamException?> CaptureAsync(ManualTimeProvider time, Task pending)
	{
		try
		{
			await DriveAsync(time, pending);
			return null;
		}
		catch (VideoStreamException exception)
		{
			return exception;
		}
	}

	private static int Count(VideoStreamFixture fixture, string operation)
		=> fixture.Invoker.Calls.Count(call => call.Operation == operation);

	private static HostInvocationException RateLimited()
		=> HostInvocationException.CreateRetryable(ProtocolErrorCodes.RateLimited, "Too many requests.");

	private IEnumerable<string> UpdateStates()
		=> _fixture.Invoker.Calls
			.Where(call => call.Operation == HostOperations.VideoStreams.SessionUpdate)
			.Select(call => ((VideoStreamsSessionUpdateArguments)call.Arguments!).State);

	private static VideoStreamSessionResumeResult Resumed(Capabilities.CapabilityInvocationResult result)
		=> result.Data!.Value.Deserialize<VideoStreamSessionResumeResult>(PluginProtocolJson.Options)!;

	private static VideoStreamProviderDescribePayload Describe(Capabilities.CapabilityInvocationResult result)
		=> result.Data!.Value.Deserialize<VideoStreamProviderDescribePayload>(PluginProtocolJson.Options)!;
}
