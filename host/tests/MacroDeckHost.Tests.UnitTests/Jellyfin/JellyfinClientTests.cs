using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using MacroDeckHost.Tests.UnitTests.HomeAssistant;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinClientTests
{
	[Test]
	public async Task Requests_authenticate_only_with_the_MediaBrowser_scheme_and_keep_the_base_path()
	{
		var handler = new RecordingHandler("[]");
		var client = Client(handler, "http://media.local/jellyfin/");

		await client.GetSessionsAsync(CancellationToken.None);

		var request = handler.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("http://media.local/jellyfin/Sessions"));
			Assert.That(request.Authorization, Does.StartWith("MediaBrowser "));
			Assert.That(request.Authorization, Does.Contain("Token=\"secret-key\""));
			Assert.That(request.Authorization, Does.Contain("DeviceId=\"device-1\""));
			Assert.That(request.HeaderNames, Has.None.EqualTo("X-Emby-Token"));
			Assert.That(request.Uri.Query, Does.Not.Contain("api_key"));
		});
	}

	[Test]
	public async Task Seeking_sends_the_position_in_Jellyfin_ticks()
	{
		var handler = new RecordingHandler(string.Empty);
		var client = Client(handler);

		await client.SendPlaystateAsync("s1", "Seek", TimeSpan.FromSeconds(90).Ticks, CancellationToken.None);

		Assert.That(handler.Requests.Single().Uri.PathAndQuery,
			Is.EqualTo("/Sessions/s1/Playing/Seek?seekPositionTicks=900000000"));
	}

	[Test]
	public async Task General_commands_send_their_arguments_as_strings()
	{
		var handler = new RecordingHandler(string.Empty);
		var client = Client(handler);

		await client.SendGeneralCommandAsync("s1",
			"SetVolume",
			new Dictionary<string, string> { ["Volume"] = "35" },
			CancellationToken.None);

		using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
		Assert.Multiple(() =>
		{
			Assert.That(body.RootElement.GetProperty("Name").GetString(), Is.EqualTo("SetVolume"));
			Assert.That(body.RootElement.GetProperty("Arguments").GetProperty("Volume").ValueKind,
				Is.EqualTo(JsonValueKind.String));
		});
	}

	[Test]
	public void A_rejected_token_is_reported_as_an_authentication_failure()
	{
		var client = Client(new RecordingHandler(string.Empty, HttpStatusCode.Unauthorized));

		Assert.ThrowsAsync<JellyfinAuthenticationException>(() => client.GetSessionsAsync(CancellationToken.None));
	}

	[Test]
	public async Task The_session_socket_subscribes_and_delivers_session_lists()
	{
		using var pair = await WebSocketPair.CreateAsync();
		Uri? connectedTo = null;
		string? authorization = null;
		var client = new JellyfinClient(new JellyfinServerSettings(new Uri("https://media.local/jellyfin"), "secret-key", "device-1"),
			new HttpClient(new RecordingHandler("[]")),
			(uri, header, _) =>
			{
				connectedTo = uri;
				authorization = header;
				return Task.FromResult(pair.Client);
			});

		var received = new TaskCompletionSource<IReadOnlyList<JellyfinSessionDto>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var run = client.RunSessionSocketAsync(sessions => received.TrySetResult(sessions), cts.Token);

		var subscribe = await pair.ReceiveAsync();
		await pair.SendAsync("""{"MessageType":"Sessions","Data":[{"Id":"s1","DeviceId":"d1","DeviceName":"TV"}]}""");
		var sessions = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await cts.CancelAsync();
		try
		{
			await run;
		}
		catch (OperationCanceledException)
		{
		}

		using var message = JsonDocument.Parse(subscribe);
		Assert.Multiple(() =>
		{
			Assert.That(connectedTo?.AbsoluteUri, Is.EqualTo("wss://media.local/jellyfin/socket"));
			Assert.That(authorization, Does.StartWith("MediaBrowser ").And.Contain("Token=\"secret-key\""));
			Assert.That(message.RootElement.GetProperty("MessageType").GetString(), Is.EqualTo("SessionsStart"));
			Assert.That(sessions.Single().DeviceName, Is.EqualTo("TV"));
		});
	}

	[Test]
	public async Task A_server_that_keeps_the_socket_open_but_sends_no_sessions_is_reported_as_unavailable()
	{
		using var pair = await WebSocketPair.CreateAsync();
		var client = new JellyfinClient(new JellyfinServerSettings(new Uri("http://media.local"), "key", "device-1"),
			new HttpClient(new RecordingHandler("[]")),
			(_, _, _) => Task.FromResult(pair.Client),
			subscriptionTimeout: TimeSpan.FromMilliseconds(200));

		var run = client.RunSessionSocketAsync(_ => { }, CancellationToken.None);
		await pair.ReceiveAsync();
		await pair.SendAsync("""{"MessageType":"ForceKeepAlive","Data":60}""");

		Assert.ThrowsAsync<JellyfinSocketException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)));
	}

	private static JellyfinClient Client(RecordingHandler handler, string baseUrl = "http://media.local:8096")
		=> new(new JellyfinServerSettings(new Uri(baseUrl), "secret-key", "device-1"),
			new HttpClient(handler),
			(_, _, _) => Task.FromException<WebSocket>(new WebSocketException("no socket")));

	private sealed record RecordedRequest(Uri Uri, string? Authorization, IReadOnlyList<string> HeaderNames, string? Body);

	private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
	{
		public List<RecordedRequest> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			Requests.Add(new RecordedRequest(request.RequestUri!,
				request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null,
				[.. request.Headers.Select(header => header.Key)],
				content));
			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
		}
	}
}
