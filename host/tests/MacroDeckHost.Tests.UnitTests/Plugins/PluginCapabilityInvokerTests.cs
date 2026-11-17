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
	public void TearDown()
	{
		_invoker.Dispose();
	}

	private static CapabilityInvokeRequest Request(
		string kind = "actions",
		string localId = "toggle",
		string operation = "execute")
	{
		return new CapabilityInvokeRequest { Kind = kind, LocalId = localId, Operation = operation };
	}

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
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Payload = JsonSerializer.SerializeToElement(new CapabilityResultPayload { Data = data },
				PluginProtocolJson.Options)
		};
	}

	private static ProtocolEnvelope ErrorResult(string correlationId, string code, bool retryable = false)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Error = new ProtocolError { Code = code, Message = "Test.", Retryable = retryable }
		};
	}

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

	private static IReadOnlyList<ProtocolEnvelope> Invocations(FakePluginConnection connection)
	{
		return [.. connection.Sent.Where(envelope => envelope.Type == MessageTypes.CapabilityInvoke)];
	}

	private static async Task WaitUntilAsync(Func<bool> condition, string because)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(because);
			}

			await Task.Delay(10);
		}
	}

	private List<Task<JsonElement?>> Saturate(string pluginId)
	{
		return
		[
			.. Enumerable.Range(0, ProtocolLimits.MaxConcurrentInvocations)
				.Select(_ => _invoker.InvokeAsync(pluginId, Request(), CancellationToken.None))
		];
	}

	[Test]
	public async Task Calls_past_the_concurrency_limit_wait_for_a_free_slot_and_all_complete()
	{
		var connection = await AttachAsync("com.example.plugin");
		const int burst = ProtocolLimits.MaxConcurrentInvocations + 13;

		var calls = Enumerable.Range(0, burst)
			.Select(_ => _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None))
			.ToList();

		Assert.That(Invocations(connection), Has.Count.EqualTo(ProtocolLimits.MaxConcurrentInvocations));
		Assert.That(calls.Count(call => call.IsCompleted), Is.Zero, "A call past the limit failed instead of waiting.");

		var answered = new HashSet<string>(StringComparer.Ordinal);
		while (answered.Count < burst)
		{
			var outstanding = Invocations(connection).Where(envelope => !answered.Contains(envelope.Id!)).ToList();
			Assert.That(outstanding,
				Has.Count.LessThanOrEqualTo(ProtocolLimits.MaxConcurrentInvocations),
				"The plugin was sent more invocations at once than the protocol allows.");

			var next = outstanding.FirstOrDefault();
			if (next is null)
			{
				await WaitUntilAsync(() => Invocations(connection).Count > answered.Count,
					"A waiting call was never sent after a slot freed.");
				continue;
			}

			answered.Add(next.Id!);
			_invoker.TryComplete("com.example.plugin", SuccessResult(next.Id!));
		}

		await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));
		Assert.That(Invocations(connection), Has.Count.EqualTo(burst));
	}

	[Test]
	public async Task A_waiting_call_ends_with_the_callers_cancellation_and_is_never_sent()
	{
		var connection = await AttachAsync("com.example.plugin");
		var held = Saturate("com.example.plugin");
		using var cts = new CancellationTokenSource();

		var waiting = _invoker.InvokeAsync("com.example.plugin", Request(), cts.Token);
		await cts.CancelAsync();

		Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);

		foreach (var invocation in Invocations(connection))
		{
			_invoker.TryComplete("com.example.plugin", SuccessResult(invocation.Id!));
		}

		await Task.WhenAll(held);
		Assert.That(Invocations(connection), Has.Count.EqualTo(ProtocolLimits.MaxConcurrentInvocations));
	}

	[Test]
	public async Task A_call_that_waits_out_its_whole_budget_is_a_retryable_RATE_LIMITED_and_is_never_sent()
	{
		var connection = await AttachAsync("com.example.plugin");
		Saturate("com.example.plugin");

		var waiting = _invoker.InvokeAsync("com.example.plugin",
			Request() with { Timeout = TimeSpan.FromSeconds(5) },
			CancellationToken.None);

		_time.Advance(TimeSpan.FromSeconds(5));

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async () => await waiting);
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.RateLimited));
			Assert.That(exception.Retryable, Is.True);
			Assert.That(Invocations(connection), Has.Count.EqualTo(ProtocolLimits.MaxConcurrentInvocations));
		});
	}

	[Test]
	public async Task Time_spent_waiting_for_a_slot_comes_off_the_calls_budget()
	{
		var connection = await AttachAsync("com.example.plugin");
		Saturate("com.example.plugin");

		var waiting = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);

		_time.Advance(TimeSpan.FromSeconds(10));
		_invoker.TryComplete("com.example.plugin", SuccessResult(Invocations(connection)[0].Id!));

		await WaitUntilAsync(() => Invocations(connection).Count > ProtocolLimits.MaxConcurrentInvocations,
			"The waiting call was never sent after a slot freed.");
		var sent = Invocations(connection)[^1];

		var remaining = ProtocolTimeouts.CapabilityInvoke - TimeSpan.FromSeconds(10);
		Assert.That(sent.DeadlineMs, Is.EqualTo((int)remaining.TotalMilliseconds));

		_time.Advance(remaining);

		var exception = Assert.ThrowsAsync<RemoteCapabilityException>(async () => await waiting);
		Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
	}

	[Test]
	public async Task Disposing_the_invoker_ends_a_waiting_call_without_sending_it()
	{
		var connection = await AttachAsync("com.example.plugin");
		Saturate("com.example.plugin");

		var waiting = _invoker.InvokeAsync("com.example.plugin", Request(), CancellationToken.None);

		_invoker.Dispose();

		Assert.ThrowsAsync<ObjectDisposedException>(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.That(Invocations(connection), Has.Count.EqualTo(ProtocolLimits.MaxConcurrentInvocations));
	}

	[Test]
	public async Task A_plugin_at_its_limit_does_not_hold_up_another_plugins_call()
	{
		await AttachAsync("com.example.plugin-a");
		var connectionB = await AttachAsync("com.example.plugin-b");
		Saturate("com.example.plugin-a");
		var waitingOnA = _invoker.InvokeAsync("com.example.plugin-a", Request(), CancellationToken.None);

		var callB = _invoker.InvokeAsync("com.example.plugin-b", Request(), CancellationToken.None);

		Assert.That(Invocations(connectionB), Has.Count.EqualTo(1));
		_invoker.TryComplete("com.example.plugin-b", SuccessResult(Invocations(connectionB)[0].Id!));
		await callB.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.That(waitingOnA.IsCompleted, Is.False);
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
