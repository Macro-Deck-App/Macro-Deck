using System.Net.WebSockets;
using System.Security.Claims;
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
}
