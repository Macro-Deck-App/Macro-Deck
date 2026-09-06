using System.Net.WebSockets;
using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsJsonRpcClientTests
{
	private static readonly Uri _uri = new("ws://127.0.0.1:59650/api/websocket");

	private WebSocketPair _pair = null!;
	private StreamlabsJsonRpcClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new StreamlabsJsonRpcClient((_, _) => Task.FromResult(_pair.Client),
			TimeSpan.FromSeconds(3));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	[Test]
	public async Task ConnectAsync_SendsAuthFirstAndCompletesOnTrue()
	{
		var connect = _client.ConnectAsync(_uri, "secret", CancellationToken.None);

		var request = await _pair.ReceiveAsync();
		Assert.Multiple(() =>
		{
			Assert.That(request, Does.Contain("\"method\":\"auth\""));
			Assert.That(request, Does.Contain("\"resource\":\"TcpServerService\""));
			Assert.That(request, Does.Contain("secret"));
		});

		await _pair.SendAsync(Reply(request, "true"));
		await connect;

		Assert.That(_client.IsConnected, Is.True);
	}

	[Test]
	public async Task ConnectAsync_TreatsAFalseResultAsARejectedToken()
	{
		var connect = _client.ConnectAsync(_uri, "bad", CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Reply(request, "false"));

		Assert.That(async () => await connect, Throws.TypeOf<StreamlabsAuthenticationException>());
	}

	[Test]
	public async Task ConnectAsync_TreatsAnErrorReplyAsARejectedToken()
	{
		var connect = _client.ConnectAsync(_uri, "bad", CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Error(request, -32000, "Invalid token"));

		Assert.That(async () => await connect, Throws.TypeOf<StreamlabsAuthenticationException>());
	}

	[Test]
	public async Task ConnectAsync_RefusesASecondSession()
	{
		await ConnectAsync();

		Assert.That(async () => await _client.ConnectAsync(_uri, "secret", CancellationToken.None),
			Throws.TypeOf<InvalidOperationException>());
	}

	[Test]
	public async Task InvokeAsync_ResolvesConcurrentCallsOutOfOrder()
	{
		await ConnectAsync();

		var first = _client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
		var firstRequest = await _pair.ReceiveAsync();
		var second = _client.InvokeAsync("SourcesService", "getSources", null, CancellationToken.None);
		var secondRequest = await _pair.ReceiveAsync();

		Assert.That(IdOf(firstRequest), Is.Not.EqualTo(IdOf(secondRequest)));

		await _pair.SendAsync(Reply(secondRequest, """["sources"]"""));
		await _pair.SendAsync(Reply(firstRequest, """["scenes"]"""));

		Assert.Multiple(async () =>
		{
			Assert.That((await first)[0].GetString(), Is.EqualTo("scenes"));
			Assert.That((await second)[0].GetString(), Is.EqualTo("sources"));
		});
	}

	[Test]
	public async Task InvokeAsync_AwaitsThePromiseFollowUp()
	{
		await ConnectAsync();

		var invoke = _client.InvokeAsync("StreamingService", "toggleStreaming", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();

		await _pair.SendAsync(Reply(request,
			"""{"_type":"SUBSCRIPTION","resourceId":"promise-1","emitter":"PROMISE"}"""));

		Assert.That(invoke.IsCompleted, Is.False, "the promise handle is not the answer");

		await _pair.SendAsync(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"PROMISE","resourceId":"promise-1","data":{"ok":true},"isRejected":false}}""");

		var result = await invoke;
		Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
	}

	[Test]
	public async Task InvokeAsync_ThrowsWhenThePromiseIsRejected()
	{
		await ConnectAsync();

		var invoke = _client.InvokeAsync("StreamingService", "toggleStreaming", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();

		await _pair.SendAsync(Reply(request,
			"""{"_type":"SUBSCRIPTION","resourceId":"promise-2","emitter":"PROMISE"}"""));
		await _pair.SendAsync(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"PROMISE","resourceId":"promise-2","data":{"message":"no stream key"},"isRejected":true}}""");

		Assert.That(async () => await invoke,
			Throws.TypeOf<StreamlabsRpcException>().With.Message.Contains("no stream key"));
	}

	[Test]
	public async Task InvokeAsync_DeliversAPromiseFollowUpThatArrivesImmediately()
	{
		await ConnectAsync();

		var invoke = _client.InvokeAsync("StreamingService", "toggleStreaming", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();

		await _pair.SendAsync(Reply(request,
			"""{"_type":"SUBSCRIPTION","resourceId":"promise-3","emitter":"PROMISE"}"""));
		await _pair.SendAsync(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"PROMISE","resourceId":"promise-3","data":{"ok":true},"isRejected":false}}""");

		var result = await invoke;
		Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
	}

	[Test]
	public async Task InvokeAsync_SurfacesAJsonRpcError()
	{
		await ConnectAsync();

		var invoke = _client.InvokeAsync("ScenesService", "nope", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Error(request, -32601, "Method not found"));

		Assert.That(async () => await invoke,
			Throws.TypeOf<StreamlabsRpcException>().With.Message.Contains("Method not found"));
	}

	[Test]
	public async Task SubscribeAsync_ReturnsTheResourceIdAndLaterEventsRaise()
	{
		await ConnectAsync();

		var received = new List<StreamlabsEvent>();
		_client.EventReceived += (_, push) => received.Add(push);

		var subscribe = _client.SubscribeAsync("ScenesService", "sceneSwitched", CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Reply(request,
			"""{"_type":"SUBSCRIPTION","resourceId":"ScenesService.sceneSwitched","emitter":"STREAM"}"""));

		Assert.That(await subscribe, Is.EqualTo("ScenesService.sceneSwitched"));

		await _pair.SendAsync(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"STREAM","resourceId":"ScenesService.sceneSwitched","data":{"id":"s1","name":"Gameplay"}}}""");

		await WaitUntil(() => received.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(received[0].ResourceId, Is.EqualTo("ScenesService.sceneSwitched"));
			Assert.That(received[0].Data.GetProperty("name").GetString(), Is.EqualTo("Gameplay"));
		});
	}

	[Test]
	public async Task ClosingTheSocket_FaultsPendingCallsAndReportsDisconnectOnce()
	{
		await ConnectAsync();

		var reasons = new List<string?>();
		_client.Disconnected += (_, reason) => reasons.Add(reason);

		var invoke = _client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
		await _pair.ReceiveAsync();

		_pair.Break();

		Assert.That(async () => await invoke, Throws.TypeOf<StreamlabsRpcException>());
		await WaitUntil(() => reasons.Count > 0);

		_client.Dispose();
		Assert.That(reasons, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task InvokeAsync_FailsWhenTheClientIsNotConnected()
	{
		Assert.That(async () => await _client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None),
			Throws.TypeOf<StreamlabsRpcException>());

		await Task.CompletedTask;
	}

	[Test]
	public async Task ABinaryFrame_IsDroppedWithoutEndingTheSession()
	{
		await ConnectAsync();

		await _pair.Server.SendAsync(new byte[] { 1, 2, 3 },
			WebSocketMessageType.Binary,
			endOfMessage: true,
			CancellationToken.None);

		var invoke = _client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Reply(request, """["still here"]"""));

		Assert.That((await invoke)[0].GetString(), Is.EqualTo("still here"));
	}

	[Test]
	public async Task AMalformedFrame_IsDroppedWithoutEndingTheSession()
	{
		await ConnectAsync();

		await _pair.SendAsync("this is not json");

		var invoke = _client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Reply(request, """["still here"]"""));

		Assert.That((await invoke)[0].GetString(), Is.EqualTo("still here"));
	}

	[Test]
	public async Task InvokeAsync_TimesOutWithoutLeakingThePendingCall()
	{
		var client = new StreamlabsJsonRpcClient((_, _) => Task.FromResult(_pair.Client),
			TimeSpan.FromMilliseconds(200));
		try
		{
			var connect = client.ConnectAsync(_uri, "secret", CancellationToken.None);
			var request = await _pair.ReceiveAsync();
			await _pair.SendAsync(Reply(request, "true"));
			await connect;

			var invoke = client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
			await _pair.ReceiveAsync();

			Assert.That(async () => await invoke,
				Throws.TypeOf<StreamlabsRpcException>().With.Message.Contains("did not answer"));

			var second = client.InvokeAsync("ScenesService", "getScenes", null, CancellationToken.None);
			var secondRequest = await _pair.ReceiveAsync();
			await _pair.SendAsync(Reply(secondRequest, """["ok"]"""));

			Assert.That((await second)[0].GetString(), Is.EqualTo("ok"));
		}
		finally
		{
			client.Dispose();
		}
	}

	private async Task ConnectAsync()
	{
		var connect = _client.ConnectAsync(_uri, "secret", CancellationToken.None);
		var request = await _pair.ReceiveAsync();
		await _pair.SendAsync(Reply(request, "true"));
		await connect;
	}

	private static string Reply(string request, string result)
		=> $$$"""{"jsonrpc":"2.0","id":{{{IdOf(request)}}},"result":{{{result}}}}""";

	private static string Error(string request, int code, string message)
		=> $$$"""{"jsonrpc":"2.0","id":{{{IdOf(request)}}},"error":{"code":{{{code}}},"message":"{{{message}}}"}}""";

	private static long IdOf(string request)
	{
		using var document = JsonDocument.Parse(request);
		return document.RootElement.GetProperty("id").GetInt64();
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 100 && !condition(); attempt++)
		{
			await Task.Delay(20);
		}

		Assert.That(condition(), Is.True, "the expected state was never reached");
	}
}
