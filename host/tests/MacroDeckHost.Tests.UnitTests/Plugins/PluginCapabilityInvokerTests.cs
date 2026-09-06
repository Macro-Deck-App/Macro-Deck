using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginCapabilityInvokerTests
{
	private ManualTimeProvider _time = null!;
	private PluginSessionRegistry _registry = null!;
	private PluginCapabilityInvoker _invoker = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_invoker = new PluginCapabilityInvoker(_registry, _time, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown() => _invoker.Dispose();

	private static CapabilityInvokeRequest Request(string kind = "actions",
		string localId = "toggle",
		string operation = "execute")
		=> new() { Kind = kind, LocalId = localId, Operation = operation };

	private async Task<FakePluginConnection> AttachAsync(string pluginId)
	{
		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = pluginId,
			DisplayName = "Example",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = _time.GetUtcNow()
		};

		await _registry.Create(record);
		var connection = new FakePluginConnection();
		_registry.TryAttach(record.SessionId, connection, null);
		return connection;
	}

	private static ProtocolEnvelope SuccessResult(string correlationId, JsonElement? data = null)
		=> new()
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Payload = JsonSerializer.SerializeToElement(new CapabilityResultPayload { Data = data },
				PluginProtocolJson.Options)
		};

	private static ProtocolEnvelope ErrorResult(string correlationId, string code, bool retryable = false)
		=> new()
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Error = new ProtocolError { Code = code, Message = "Test.", Retryable = retryable }
		};

	[Test]
	public async Task A_successful_round_trip_returns_the_result_payload()
	{
		var connection = await AttachAsync("com.example.plugin");

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);
		var correlationId = connection.Sent.Single().Id;

		var data = JsonSerializer.SerializeToElement(new { ok = true });
		var completed = _invoker.TryComplete("com.example.plugin", SuccessResult(correlationId, data));

		Assert.That(completed, Is.True);

		var result = await invokeTask;

		Assert.That(result!.Value.GetProperty("ok").GetBoolean(), Is.True);
	}

	[Test]
	public async Task The_timeout_budget_expires_into_a_retryable_TIMEOUT()
	{
		await AttachAsync("com.example.plugin");

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);

		_time.Advance(ProtocolTimeouts.CapabilityInvoke);

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async () => await invokeTask);

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
			Assert.That(exception.Retryable, Is.True);
		});
	}

	[Test]
	public async Task InvokeAsync_times_out_even_when_the_send_itself_never_completes()
	{
		// Regression for issue #413 finding 1: a plugin that stops draining its socket parks
		// connection.Send forever. Before the fix, InvokeAsync awaited SendToPlugin before the timeout
		// race even started, so a caller passing CancellationToken.None (the broadcast and polling
		// background services both do) - and the concurrency slot it held - would never come back.
		var connection = await AttachAsync("com.example.plugin");
		connection.SendGate = new TaskCompletionSource<bool>();

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);

		_time.Advance(ProtocolTimeouts.CapabilityInvoke);

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async () => await invokeTask);
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
			Assert.That(exception.Retryable, Is.True);
		});

		connection.SendGate.TrySetResult(true);
	}

	[Test]
	public async Task Caller_cancellation_sends_capability_cancel_and_returns_promptly_without_the_plugins_reply()
	{
		var connection = await AttachAsync("com.example.plugin");
		using var cts = new CancellationTokenSource();

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), cts.Token);
		var correlationId = connection.Sent.Single().Id;

		await cts.CancelAsync();

		Assert.ThrowsAsync<OperationCanceledException>(async () => await invokeTask);

		var cancel = connection.Sent.FirstOrDefault(e => e.Type == MessageTypes.CapabilityCancel);
		Assert.Multiple(() =>
		{
			Assert.That(cancel, Is.Not.Null, "capability.cancel should have been sent on the wire");
			Assert.That(cancel!.CorrelationId, Is.EqualTo(correlationId));
		});

		// A reply arriving after the caller gave up must not be reported as an error.
		var lateReply = _invoker.TryComplete("com.example.plugin", SuccessResult(correlationId));
		Assert.That(lateReply, Is.True);
	}

	[Test]
	public void TryComplete_Reports_An_Unknown_Correlation()
	{
		var handled = _invoker.TryComplete("com.example.plugin",
			SuccessResult(Guid.CreateVersion7().ToString()));

		Assert.That(handled, Is.False);
	}

	[Test]
	public async Task A_late_result_after_abandonment_is_dropped_silently()
	{
		var connection = await AttachAsync("com.example.plugin");

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);
		var correlationId = connection.Sent.Single().Id;

		_time.Advance(ProtocolTimeouts.CapabilityInvoke);
		Assert.ThrowsAsync<RemoteCapabilityException>(async () => await invokeTask);

		var handled = _invoker.TryComplete("com.example.plugin", SuccessResult(correlationId));

		Assert.That(handled, Is.True);
	}

	[Test]
	public async Task A_result_whose_correlation_belongs_to_another_plugin_is_rejected()
	{
		var connectionA = await AttachAsync("com.example.plugin-a");
		await AttachAsync("com.example.plugin-b");

		var invokeTask = _invoker.InvokeAsync("com.example.plugin-a", Request(), CancellationToken.None);
		var correlationId = connectionA.Sent.Single().Id;

		var rejected = _invoker.TryComplete("com.example.plugin-b", SuccessResult(correlationId));
		Assert.That(rejected, Is.False);

		_invoker.TryComplete("com.example.plugin-a", SuccessResult(correlationId));
		await invokeTask;
	}

	[Test]
	public async Task Exhausting_The_Concurrency_Gate_Yields_A_Retryable_RATE_LIMITED()
	{
		await AttachAsync("com.example.plugin");

		var pending = new List<Task>();
		for (var i = 0; i < ProtocolLimits.MaxConcurrentInvocations; i++)
		{
			pending.Add(_invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None));
		}

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async ()
			=> await _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(exception.Retryable, Is.True);
		});

		_invoker.AbortAll("com.example.plugin",
			new ProtocolError
				{ Code = ProtocolErrorCodes.CapabilityUnavailable, Message = "Test cleanup.", Retryable = true });

		foreach (var task in pending)
		{
			Assert.ThrowsAsync<RemoteCapabilityException>(async () => await task);
		}
	}

	[Test]
	public async Task AbortAll_Faults_Everything_Pending_For_That_Plugin_And_Nothing_For_Another()
	{
		var connectionA = await AttachAsync("com.example.plugin-a");
		var connectionB = await AttachAsync("com.example.plugin-b");

		var taskA = _invoker.InvokeAsync("com.example.plugin-a", Request(), CancellationToken.None);
		var taskB = _invoker.InvokeAsync("com.example.plugin-b", Request(), CancellationToken.None);

		_invoker.AbortAll("com.example.plugin-a",
			new ProtocolError
				{ Code = ProtocolErrorCodes.CapabilityUnavailable, Message = "Dropped.", Retryable = true });

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async () => await taskA);
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		Assert.That(taskB.IsCompleted, Is.False);

		var correlationB = connectionB.Sent.Single().Id;
		_invoker.TryComplete("com.example.plugin-b", SuccessResult(correlationB));
		await taskB;

		Assert.That(connectionA.Sent, Has.Count.EqualTo(1), "only the original invoke, no cancel round trip");
	}

	[Test]
	public async Task CANCELLED_From_The_Plugin_Surfaces_As_OperationCanceledException()
	{
		var connection = await AttachAsync("com.example.plugin");

		var invokeTask = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);
		var correlationId = connection.Sent.Single().Id;

		_invoker.TryComplete("com.example.plugin", ErrorResult(correlationId, ProtocolErrorCodes.Cancelled));

		Assert.ThrowsAsync<OperationCanceledException>(async () => await invokeTask);
	}
}
