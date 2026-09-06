using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopRealtimeClientTests
{
	private static readonly Uri _uri = new("ws://127.0.0.1:9863/socket.io/?EIO=4&transport=websocket");

	private WebSocketPair _pair = null!;
	private YtmDesktopRealtimeClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new YtmDesktopRealtimeClient((_, _) => Task.FromResult(_pair.Client),
			handshakeTimeout: TimeSpan.FromMilliseconds(500));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	private static string OpenFrame(int pingIntervalMs = 25000, int pingTimeoutMs = 20000)
	{
		var interval = pingIntervalMs.ToString(CultureInfo.InvariantCulture);
		var timeout = pingTimeoutMs.ToString(CultureInfo.InvariantCulture);
		return $$"""0{"sid":"s1","pingInterval":{{interval}},"pingTimeout":{{timeout}}}""";
	}

	private async Task ConnectClientAsync(string token = "tok")
	{
		var connecting = _client.ConnectAsync(_uri, token, CancellationToken.None);
		await _pair.SendAsync(OpenFrame());
		await _pair.ReceiveAsync();
		await _pair.SendAsync("""40/api/v1/realtime,{"sid":"s1"}""");
		await connecting;
	}

	[Test]
	public async Task ConnectAsync_sends_connect_only_after_open_and_carries_the_token()
	{
		var connecting = _client.ConnectAsync(_uri, "secret-token", CancellationToken.None);

		var receiving = _pair.ReceiveAsync();
		var silentSoFar = await Task.WhenAny(receiving, Task.Delay(TimeSpan.FromMilliseconds(100))) != receiving;
		Assert.That(silentSoFar, Is.True, "nothing must be sent before the Engine.IO OPEN frame arrives");

		await _pair.SendAsync(OpenFrame());

		var frame = await receiving;
		Assert.That(frame, Is.EqualTo("""40/api/v1/realtime,{"token":"secret-token"}"""));

		await _pair.SendAsync("""40/api/v1/realtime,{"sid":"s1"}""");

		Assert.DoesNotThrowAsync(async () => await connecting);
		Assert.That(_client.IsConnected, Is.True);
	}

	[Test]
	public void ConnectAsync_times_out_when_open_never_arrives()
	{
		Assert.ThrowsAsync<YtmDesktopApiException>(async () =>
			await _client.ConnectAsync(_uri, "tok", CancellationToken.None));
	}

	[Test]
	public async Task ConnectAsync_on_an_already_used_instance_throws()
	{
		await ConnectClientAsync();

		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await _client.ConnectAsync(_uri, "tok", CancellationToken.None));
	}

	[TestCase("""{"message":"UNAUTHENTICATED"}""")]
	[TestCase("""{"code":"UNAUTHENTICATED"}""")]
	[TestCase("UNAUTHENTICATED")]
	public async Task ConnectAsync_throws_authorization_exception_on_an_unauthenticated_connect_error(string payload)
	{
		var connecting = _client.ConnectAsync(_uri, "bad-token", CancellationToken.None);

		await _pair.SendAsync(OpenFrame());
		await _pair.ReceiveAsync();
		await _pair.SendAsync($"44/api/v1/realtime,{payload}");

		Assert.ThrowsAsync<YtmDesktopAuthorizationException>(async () => await connecting);
	}

	[Test]
	public async Task ConnectAsync_throws_api_exception_on_a_non_authentication_connect_error()
	{
		var connecting = _client.ConnectAsync(_uri, "tok", CancellationToken.None);

		await _pair.SendAsync(OpenFrame());
		await _pair.ReceiveAsync();
		await _pair.SendAsync("""44/api/v1/realtime,{"message":"something else"}""");

		var exception = Assert.ThrowsAsync<YtmDesktopApiException>(async () => await connecting);
		Assert.That(exception, Is.Not.InstanceOf<YtmDesktopAuthorizationException>());
	}

	[Test]
	public async Task StateUpdated_argument_is_still_readable_after_the_read_loop_moves_on()
	{
		await ConnectClientAsync();

		var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.StateUpdated += (_, state) => received.TrySetResult(state);

		await _pair.SendAsync("""42/api/v1/realtime,["state-update",{"player":{"trackState":1}}]""");
		var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await _pair.SendAsync("2");
		var pong = await _pair.ReceiveAsync();
		Assert.That(pong, Is.EqualTo("3"));

		Assert.That(state.GetProperty("player").GetProperty("trackState").GetInt32(), Is.EqualTo(1));
	}

	[Test]
	public async Task A_ping_is_answered_with_a_pong()
	{
		await ConnectClientAsync();

		await _pair.SendAsync("2");
		var reply = await _pair.ReceiveAsync();

		Assert.That(reply, Is.EqualTo("3"));
	}

	[Test]
	public async Task A_broken_socket_raises_disconnected_exactly_once()
	{
		await ConnectClientAsync();

		var disconnects = 0;
		var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.Disconnected += (_, _) =>
		{
			Interlocked.Increment(ref disconnects);
			disconnected.TrySetResult(true);
		};

		_pair.Break();
		await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

		_client.Dispose();
		Assert.That(disconnects, Is.EqualTo(1));
	}

	[Test]
	public async Task A_malformed_frame_does_not_end_the_session()
	{
		await ConnectClientAsync();

		await _pair.SendAsync("not a valid frame at all");

		await _pair.SendAsync("2");
		var reply = await _pair.ReceiveAsync();

		Assert.That(reply, Is.EqualTo("3"));
	}

	[Test]
	public async Task Playlist_created_and_deleted_events_are_raised()
	{
		await ConnectClientAsync();

		var created = new TaskCompletionSource<YtmPlaylist>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.PlaylistCreated += (_, playlist) => created.TrySetResult(playlist);

		var deleted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.PlaylistDeleted += (_, id) => deleted.TrySetResult(id);

		await _pair.SendAsync("""42/api/v1/realtime,["playlist-created",{"id":"pl-1","title":"My Playlist"}]""");
		var createdPlaylist = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

		await _pair.SendAsync("""42/api/v1/realtime,["playlist-deleted","pl-1"]""");
		var deletedId = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(createdPlaylist.Id, Is.EqualTo("pl-1"));
			Assert.That(createdPlaylist.Title, Is.EqualTo("My Playlist"));
			Assert.That(deletedId, Is.EqualTo("pl-1"));
		});
	}

	[Test]
	public async Task The_watchdog_disconnects_when_no_ping_arrives_within_the_servers_own_interval()
	{
		var connecting = _client.ConnectAsync(_uri, "tok", CancellationToken.None);
		await _pair.SendAsync(OpenFrame(pingIntervalMs: 20, pingTimeoutMs: 20));
		await _pair.ReceiveAsync();
		await _pair.SendAsync("""40/api/v1/realtime,{"sid":"s1"}""");
		await connecting;

		var disconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.Disconnected += (_, reason) => disconnected.TrySetResult(reason);

		var reason = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(reason, Is.EqualTo("ping timeout"));
	}
}
