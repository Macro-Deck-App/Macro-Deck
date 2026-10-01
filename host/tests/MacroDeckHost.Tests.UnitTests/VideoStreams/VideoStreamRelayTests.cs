using MacroDeckHost.Application.VideoStreams;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamRelayTests
{
	private static readonly Uri _camera = new("https://camera.local:8443/live/cam%201/index.m3u8?key=a%2Fb&x=1#frag");

	private static string TokenOf(string relayUrl)
		=> relayUrl[VideoStreamRelay.PathPrefix.Length..].Split('/')[0];

	private static VideoStreamRelayLease Acquire(VideoStreamRelay relay, string token)
	{
		Assert.That(relay.Acquire(token, out var lease), Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
		return lease!;
	}

	[Test]
	public void The_published_path_is_an_unguessable_host_path_that_carries_the_sources_path_and_query()
	{
		var relay = new VideoStreamRelay();

		var first = relay.Arm("s1", _camera, "hls");
		var second = relay.Arm("s2", _camera, "hls");

		Assert.Multiple(() =>
		{
			Assert.That(first, Does.StartWith("/api/video-streams/relay/"));
			Assert.That(first, Does.EndWith("/live/cam%201/index.m3u8?key=a%2Fb&x=1"));
			Assert.That(TokenOf(first), Has.Length.EqualTo(43).And.Matches("^[A-Za-z0-9_-]+$"));
			Assert.That(TokenOf(second), Is.Not.EqualTo(TokenOf(first)));
			Assert.That(first, Does.Not.Contain("camera.local"));
		});
	}

	[Test]
	public void A_token_serves_only_the_session_it_was_armed_for_and_nothing_made_up()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));

		using var lease = Acquire(relay, token);

		Assert.Multiple(() =>
		{
			Assert.That(lease.Upstream, Is.EqualTo(_camera));
			Assert.That(lease.Transport, Is.EqualTo("hls"));
			Assert.That(relay.Acquire("s1", out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(relay.Acquire(token + "x", out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(relay.Acquire("", out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
		});
	}

	[Test]
	public void Arming_again_keeps_the_token_and_serves_the_new_source_from_then_on()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));
		var elsewhere = new Uri("http://10.0.0.5:8080/stream.mjpg");

		var republished = relay.Arm("s1", elsewhere, "mjpeg");
		using var lease = Acquire(relay, token);

		Assert.Multiple(() =>
		{
			Assert.That(TokenOf(republished), Is.EqualTo(token));
			Assert.That(republished, Does.EndWith("/stream.mjpg"));
			Assert.That(lease.Upstream, Is.EqualTo(elsewhere));
			Assert.That(lease.Transport, Is.EqualTo("mjpeg"));
		});
	}

	[Test]
	public void A_request_to_a_source_that_was_replaced_is_aborted_but_one_to_a_source_that_stayed_is_not()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));
		using var stays = Acquire(relay, token);

		relay.Arm("s1", new Uri("https://camera.local:8443/other/index.m3u8"), "hls");
		var afterSameOrigin = stays.Aborted.IsCancellationRequested;
		relay.Arm("s1", new Uri("https://camera.local:9000/other/index.m3u8"), "hls");
		var afterNewPort = stays.Aborted.IsCancellationRequested;
		using var next = Acquire(relay, token);
		relay.Arm("s1", new Uri("https://camera.local:9000/other/index.m3u8"), "mjpeg");

		Assert.Multiple(() =>
		{
			Assert.That(afterSameOrigin, Is.False);
			Assert.That(afterNewPort, Is.True);
			Assert.That(next.Aborted.IsCancellationRequested, Is.True, "a transport change also ends the old requests");
		});
	}

	[Test]
	public void Suspending_aborts_what_is_in_flight_and_serves_nothing_until_the_session_is_armed_again()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));
		using var inFlight = Acquire(relay, token);

		relay.Suspend("s1");
		var whileSuspended = relay.Acquire(token, out _);
		relay.Arm("s1", _camera, "hls");
		using var resumed = Acquire(relay, token);

		Assert.Multiple(() =>
		{
			Assert.That(inFlight.Aborted.IsCancellationRequested, Is.True);
			Assert.That(whileSuspended, Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(resumed.Aborted.IsCancellationRequested, Is.False);
		});
	}

	[Test]
	public void A_revoked_token_is_dead_at_once_and_its_requests_are_aborted()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));
		using var inFlight = Acquire(relay, token);

		relay.Revoke("s1");
		relay.Revoke("s1");
		relay.Suspend("s1");

		Assert.Multiple(() =>
		{
			Assert.That(relay.Acquire(token, out _), Is.EqualTo(VideoStreamRelayAcquisition.NotFound));
			Assert.That(inFlight.Aborted.IsCancellationRequested, Is.True);
		});
	}

	[Test]
	public void A_session_serves_at_most_eight_requests_at_once_and_a_finished_one_frees_its_place()
	{
		var relay = new VideoStreamRelay();
		var token = TokenOf(relay.Arm("s1", _camera, "hls"));
		var other = TokenOf(relay.Arm("s2", _camera, "hls"));
		var held = Enumerable.Range(0, VideoStreamRelay.MaxLeasesPerSession).Select(_ => Acquire(relay, token)).ToList();

		var ninth = relay.Acquire(token, out _);
		var otherSession = relay.Acquire(other, out var otherLease);
		held[0].Dispose();
		held[0].Dispose();
		var afterRelease = relay.Acquire(token, out var replacement);
		var tenth = relay.Acquire(token, out _);

		Assert.Multiple(() =>
		{
			Assert.That(ninth, Is.EqualTo(VideoStreamRelayAcquisition.Busy));
			Assert.That(otherSession, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
			Assert.That(afterRelease, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
			Assert.That(tenth, Is.EqualTo(VideoStreamRelayAcquisition.Busy), "a double dispose frees one place only");
		});
		otherLease!.Dispose();
		replacement!.Dispose();
		held.ForEach(lease => lease.Dispose());
	}

	[Test]
	public void The_host_serves_at_most_sixty_four_requests_at_once_across_sessions()
	{
		var relay = new VideoStreamRelay();
		var tokens = Enumerable.Range(0, 9).Select(i => TokenOf(relay.Arm("s" + i, _camera, "hls"))).ToList();
		var held = new List<VideoStreamRelayLease>();
		foreach (var token in tokens.Take(8))
		{
			held.AddRange(Enumerable.Range(0, VideoStreamRelay.MaxLeasesPerSession).Select(_ => Acquire(relay, token)));
		}

		var overTheHost = relay.Acquire(tokens[8], out _);
		held[0].Dispose();
		var afterRelease = relay.Acquire(tokens[8], out var lease);

		Assert.Multiple(() =>
		{
			Assert.That(held, Has.Count.EqualTo(VideoStreamRelay.MaxLeases));
			Assert.That(overTheHost, Is.EqualTo(VideoStreamRelayAcquisition.Busy));
			Assert.That(afterRelease, Is.EqualTo(VideoStreamRelayAcquisition.Acquired));
		});
		lease!.Dispose();
		held.ForEach(l => l.Dispose());
	}
}
