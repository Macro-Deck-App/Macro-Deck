using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Ui;

// The UI WebSocket contract the desktop UI, the web client and the companion app are built against
// (ADR 0062): what a malformed peer is told, when a busy session pushes back and when a silent one is dropped.
[NonParallelizable]
public class UiWebSocketContractTests
{
	private const int RequestLimit = 256;
	private const int OutboundQueueLimit = 256;

	private readonly BlockingWeatherInstances _blocking = new();
	private readonly ManualTimeProvider _time = new();
	private InProcessHost _host = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_host = await InProcessHost.StartAsync(services =>
		{
			services.AddSingleton<TimeProvider>(_time);
			services.AddSingleton<IUiTransportMessageHandler<GetWeatherInstancesRequest, GetWeatherInstancesResponse>>(
				_blocking);
		});
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_blocking.Release();
		await _host.DisposeAsync();
	}

	[TestCase("hello", 2, TestName = "A hello for another protocol version is refused")]
	[TestCase("welcome", 1, TestName = "A first message that is not a hello is refused")]
	public async Task An_invalid_hello_closes_with_a_policy_violation(string kind, int version)
	{
		await using var client = await _host.ConnectUiAsync(null, false);

		await client.SendRawAsync(new UiWebSocketEnvelope(version, kind, null, "hello-1", null, null, null));

		Assert.That(await client.WaitForCloseAsync(), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
	}

	[TestCase("request", 2, TestName = "An envelope for another protocol version ends the session")]
	[TestCase("subscribe", 1, TestName = "An envelope of an unknown kind ends the session")]
	public async Task An_invalid_envelope_after_the_hello_closes_with_a_policy_violation(string kind, int version)
	{
		await using var client = await _host.ConnectUiAsync(null);

		await client.SendRawAsync(new UiWebSocketEnvelope(version, kind, "GetFolderViews", "r-1", null, null, null));

		Assert.That(await client.WaitForCloseAsync(), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
	}

	[Test]
	public async Task An_unknown_request_type_is_answered_and_the_session_continues()
	{
		await using var client = await _host.ConnectUiAsync(null);

		var reply = await client.RequestAsync("NotAnOperation");
		await client.SyncAsync();

		Assert.Multiple(() =>
		{
			Assert.That(reply.Kind, Is.EqualTo("error"));
			Assert.That(reply.ErrorCode, Is.EqualTo("unknown_type"));
		});
	}

	[Test]
	public async Task A_request_past_the_in_flight_limit_is_refused_as_busy_and_the_others_complete()
	{
		await using var client = await _host.ConnectUiAsync(null);
		_blocking.Block();

		var accepted = new List<string>();
		for (var index = 0; index < RequestLimit; index++)
		{
			accepted.Add(client.StartRequest("GetWeatherInstances", [], out var sent));
			await sent;
		}

		await _blocking.Entered;
		var overflow = await client.RequestAsync("GetWeatherInstances");
		_blocking.Release();
		var answered = new List<UiWsMessage>();
		foreach (var id in accepted)
		{
			answered.Add(await client.WaitForAsync(message => message.CorrelationId == id));
		}

		Assert.Multiple(() =>
		{
			Assert.That(overflow.Kind, Is.EqualTo("error"));
			Assert.That(overflow.ErrorCode, Is.EqualTo("busy"));
			Assert.That(answered.Select(message => message.Kind), Is.All.EqualTo("response"));
		});
	}

	[Test]
	public async Task A_peer_that_stays_silent_for_a_minute_is_dropped()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await using var client = new UiWsTestClient(pair.Plugin);
		await client.HelloAsync();

		_time.Advance(TimeSpan.FromSeconds(60));

		await running.WaitAsync(TimeSpan.FromSeconds(15));
		await client.WaitForCloseAsync();
	}

	[Test]
	public async Task A_peer_that_keeps_talking_is_kept_and_pinged()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await using var client = new UiWsTestClient(pair.Plugin);
		await client.HelloAsync();

		_time.Advance(TimeSpan.FromSeconds(40));
		await client.SyncAsync();
		_time.Advance(TimeSpan.FromSeconds(40));
		await client.WaitForAsync(message => message.Kind == "ping");
		await client.SyncAsync();

		Assert.That(running.IsCompleted, Is.False);
		pair.Plugin.Abort();
		await running.WaitAsync(TimeSpan.FromSeconds(15));
	}

	[Test]
	public async Task A_peer_that_stops_reading_is_aborted_once_a_push_misses_the_send_deadline()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await AttachAndStopReadingAsync(pair.Plugin);

		await PushPastTheSocketBuffersAsync();
		await Task.Delay(500);
		_time.Advance(TimeSpan.FromSeconds(30));

		await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(15)));
		Assert.Multiple(() =>
		{
			Assert.That(running.IsCompleted, Is.True, "the connection ended");
			Assert.That(pair.Host.State, Is.EqualTo(WebSocketState.Aborted));
		});
	}

	[Test]
	public async Task A_peer_that_stops_reading_is_aborted_once_its_outbound_queue_overflows()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await AttachAndStopReadingAsync(pair.Plugin);

		await PushPastTheSocketBuffersAsync();
		var transport = _host.Services.GetRequiredService<IUiTransport>();
		for (var index = 0; index <= OutboundQueueLimit && !running.IsCompleted; index++)
		{
			await transport.Send(new StalledPeerPush("small"));
		}

		await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(15)));
		Assert.Multiple(() =>
		{
			Assert.That(running.IsCompleted, Is.True, "the connection ended");
			Assert.That(pair.Host.State, Is.EqualTo(WebSocketState.Aborted));
		});
	}

	// A pong proves the session is registered for pushes; nothing is read from the socket after it.
	private static async Task AttachAndStopReadingAsync(WebSocket peer)
	{
		var client = new UiWsTestClient(peer, false);
		await client.SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"hello",
			null,
			"hello-1",
			null,
			null,
			null));
		await client.SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"ping",
			null,
			"attached-1",
			null,
			null,
			null));

		var buffer = new byte[64 * 1024];
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (true)
		{
			using var message = new MemoryStream();
			WebSocketReceiveResult result;
			do
			{
				result = await peer.ReceiveAsync(buffer, timeout.Token);
				message.Write(buffer, 0, result.Count);
			} while (!result.EndOfMessage);

			var envelope = JsonNode.Parse(message.ToArray());
			if ((string?)envelope?["kind"] == "pong" && (string?)envelope["correlationId"] == "attached-1")
			{
				return;
			}
		}
	}

	private async Task PushPastTheSocketBuffersAsync()
	{
		var transport = _host.Services.GetRequiredService<IUiTransport>();
		var text = new string('a', 900_000);
		for (var index = 0; index < 16; index++)
		{
			await transport.Send(new StalledPeerPush(text));
		}
	}

	private Task Run(WebSocket socket)
	{
		return _host.Services.GetRequiredService<UiWebSocketEndpoint>()
			.RunConnectionAsync(socket, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);
	}

	private sealed class BlockingWeatherInstances
		: IUiTransportMessageHandler<GetWeatherInstancesRequest, GetWeatherInstancesResponse>
	{
		private TaskCompletionSource _gate = Completed();
		private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Entered => _entered.Task;

		public void Block()
		{
			_gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		public void Release()
		{
			_gate.TrySetResult();
		}

		public async ValueTask<GetWeatherInstancesResponse> Handle(
			GetWeatherInstancesRequest request,
			CancellationToken cancellationToken)
		{
			_entered.TrySetResult();
			await _gate.Task.WaitAsync(cancellationToken);
			return new GetWeatherInstancesResponse();
		}

		private static TaskCompletionSource Completed()
		{
			var source = new TaskCompletionSource();
			source.SetResult();
			return source;
		}
	}

	private sealed record StalledPeerPush(string Text);
}
