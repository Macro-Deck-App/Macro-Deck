using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Integrations.Http;
using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
[NonParallelizable]
internal sealed class IntegrationHttpWireTests
{
	private HttpUserAgentState _state = null!;
	private HttpListener _listener = null!;
	private string _prefix = null!;

	[SetUp]
	public void SetUp()
	{
		_state = new HttpUserAgentState();
		IntegrationHttp.Use(_state);

		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		_prefix = $"http://127.0.0.1:{port}/";
		_listener = new HttpListener();
		_listener.Prefixes.Add(_prefix);
		_listener.Start();
	}

	[TearDown]
	public void TearDown()
	{
		_listener.Close();
		IntegrationHttp.Use(new HttpUserAgentState());
	}

	[Test]
	public async Task The_http_request_integration_client_sends_the_default_and_then_a_custom_value_without_a_restart()
	{
		var client = HttpSharedClients.Get(followRedirects: true, validateTls: true);

		var first = await GetAndCaptureUserAgent(client);
		_state.Apply("Custom/2.0");
		var second = await GetAndCaptureUserAgent(client);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(HttpUserAgent.Default));
			Assert.That(second, Is.EqualTo("Custom/2.0"));
		});
	}

	[Test]
	public async Task A_header_the_user_sets_on_an_http_request_wins_over_the_configured_value()
	{
		_state.Apply("Custom/2.0");
		var client = HttpSharedClients.Get(followRedirects: true, validateTls: true);
		using var request = new HttpRequestMessage(HttpMethod.Get, _prefix);
		request.Headers.TryAddWithoutValidation("User-Agent", "FromTheAction/1");

		var seen = await SendAndCaptureUserAgent(client, request);

		Assert.That(seen, Is.EqualTo("FromTheAction/1"));
	}

	[Test]
	public async Task A_client_made_by_the_factory_sends_the_configured_value()
	{
		_state.Apply("Custom/2.0");
		using var client = IntegrationHttp.CreateClient(TimeSpan.FromSeconds(5));

		Assert.That(await GetAndCaptureUserAgent(client), Is.EqualTo("Custom/2.0"));
	}

	[Test]
	public async Task A_websocket_upgrade_carries_the_configured_value()
	{
		_state.Apply("Custom/2.0");
		var accepted = AcceptWebSocketAndCaptureUserAgent();
		using var socket = IntegrationHttp.CreateWebSocket();

		await socket.ConnectAsync(new Uri(_prefix.Replace("http://", "ws://")), CancellationToken.None);

		Assert.That(await accepted, Is.EqualTo("Custom/2.0"));
	}

	[Test]
	public async Task A_websocket_upgrade_carries_the_default_when_nothing_is_configured()
	{
		var accepted = AcceptWebSocketAndCaptureUserAgent();
		using var socket = IntegrationHttp.CreateWebSocket();

		await socket.ConnectAsync(new Uri(_prefix.Replace("http://", "ws://")), CancellationToken.None);

		Assert.That(await accepted, Is.EqualTo(HttpUserAgent.Default));
	}

	private Task<string?> GetAndCaptureUserAgent(HttpClient client)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, _prefix);
		return SendAndCaptureUserAgent(client, request);
	}

	private async Task<string?> SendAndCaptureUserAgent(HttpClient client, HttpRequestMessage request)
	{
		var context = _listener.GetContextAsync();
		var sending = client.SendAsync(request);

		var received = await context;
		var seen = received.Request.UserAgent;
		received.Response.StatusCode = 204;
		received.Response.Close();
		(await sending).Dispose();

		return seen;
	}

	private async Task<string?> AcceptWebSocketAndCaptureUserAgent()
	{
		var context = await _listener.GetContextAsync();
		var seen = context.Request.UserAgent;
		var accepted = await context.AcceptWebSocketAsync(null);
		await accepted.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
		return seen;
	}
}
