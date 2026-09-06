using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class CapabilityDispatcherTests
{
	private readonly List<ProtocolEnvelope> _replies = [];

	[SetUp]
	public void SetUp() => _replies.Clear();

	private ValueTask Reply(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		_replies.Add(envelope);
		return ValueTask.CompletedTask;
	}

	private static ProtocolEnvelope Invoke(
		string kind = "actions",
		string localId = "one",
		string operation = "execute",
		string? idempotencyKey = null,
		int? deadlineMs = null,
		object? arguments = null)
		=> new()
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = Guid.CreateVersion7().ToString(),
			IdempotencyKey = idempotencyKey,
			DeadlineMs = deadlineMs,
			Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
			{
				Kind = kind,
				LocalId = localId,
				Operation = operation,
				Arguments = arguments is null
					? null
					: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
			})
		};

	private ProtocolEnvelope Single()
	{
		Assert.That(_replies, Has.Count.EqualTo(1), "exactly one reply per invocation is the contract");
		return _replies[0];
	}

	[Test]
	public async Task An_invocation_is_answered_with_a_correlated_result()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
		var envelope = Invoke();

		await dispatcher.DispatchAsync(envelope, Reply, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Single().Type, Is.EqualTo(MessageTypes.CapabilityResult));
			Assert.That(Single().CorrelationId, Is.EqualTo(envelope.Id));
			Assert.That(Single().Error, Is.Null);
		});
	}

	[Test]
	public async Task A_kind_no_handler_serves_is_unsupported()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));

		await dispatcher.DispatchAsync(Invoke(kind: "weather"), Reply, CancellationToken.None);

		Assert.That(Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	[Test]
	public async Task A_payload_that_does_not_bind_is_an_invalid_payload()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));

		await dispatcher.DispatchAsync(new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityInvoke,
				Id = "abc"
			},
			Reply,
			CancellationToken.None);

		Assert.That(Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task Each_invocation_runs_in_its_own_scope()
	{
		IServiceProvider? first = null;
		IServiceProvider? second = null;

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			(invocation, _) =>
			{
				if (first is null)
				{
					first = invocation.Services;
				}
				else
				{
					second = invocation.Services;
				}

				return Task.FromResult(CapabilityInvocationResult.Ok());
			}));

		await dispatcher.DispatchAsync(Invoke(), Reply, CancellationToken.None);
		await dispatcher.DispatchAsync(Invoke(), Reply, CancellationToken.None);

		Assert.That(first, Is.Not.SameAs(second));
	}

	[Test]
	public async Task A_handler_that_throws_produces_a_redacted_internal_error()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			(_, _) => throw new InvalidOperationException("token=abcdef at /home/user/secret.json")));

		await dispatcher.DispatchAsync(Invoke(), Reply, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));

			// The exception message never reaches the wire: it can carry a path, a query or a token.
			Assert.That(Single().Error!.Message, Does.Not.Contain("abcdef"));
			Assert.That(Single().Error!.Message, Does.Not.Contain("secret.json"));
		});
	}

	[Test]
	public async Task An_invocation_that_outlives_its_deadline_times_out_exactly_once()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, token) =>
			{
				await Task.Delay(Timeout.Infinite, token);
				return CapabilityInvocationResult.Ok();
			}));

		await dispatcher.DispatchAsync(Invoke(deadlineMs: 20), Reply, CancellationToken.None);

		Assert.That(Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
	}

	[Test]
	public async Task A_cancel_produces_exactly_one_cancelled_result()
	{
		var running = new TaskCompletionSource();

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, token) =>
			{
				running.TrySetResult();
				await Task.Delay(Timeout.Infinite, token);
				return CapabilityInvocationResult.Ok();
			}));

		var envelope = Invoke();
		var dispatch = dispatcher.DispatchAsync(envelope, Reply, CancellationToken.None);
		await running.Task;

		var cancelled = dispatcher.Cancel(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityCancel,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id
		});

		await dispatch;

		Assert.Multiple(() =>
		{
			Assert.That(cancelled!.Error!.Code, Is.EqualTo(ProtocolErrorCodes.Cancelled));

			// The cancel path owns the single reply, so the handler's own completion must not add one.
			Assert.That(_replies, Is.Empty);
		});
	}

	[Test]
	public void A_cancel_for_an_unknown_correlation_is_a_no_op()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));

		var result = dispatcher.Cancel(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityCancel,
			Id = "x",
			CorrelationId = "never-seen"
		});

		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task A_cancel_after_the_result_was_sent_is_a_no_op()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
		var envelope = Invoke();

		await dispatcher.DispatchAsync(envelope, Reply, CancellationToken.None);

		var result = dispatcher.Cancel(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityCancel,
			Id = "x",
			CorrelationId = envelope.Id
		});

		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task A_repeated_idempotency_key_replays_the_first_result_without_re_running()
	{
		var handler = new TestCapabilityHandler("actions");
		using var dispatcher = TestSession.Dispatcher(handler);

		await dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);
		await dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(handler.Invocations, Is.EqualTo(1));
			Assert.That(_replies, Has.Count.EqualTo(2));
			Assert.That(_replies[1].Error, Is.Null);
		});
	}

	[Test]
	public async Task A_repeated_idempotency_key_while_the_first_is_in_flight_is_refused()
	{
		var running = new TaskCompletionSource();
		var release = new TaskCompletionSource();

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				running.TrySetResult();
				await release.Task;
				return CapabilityInvocationResult.Ok();
			}));

		var first = dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);
		await running.Task;

		await dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);

		// Specified behaviour: a repeat while the original runs fails rather than joining it, so the
		// caller learns its retry was premature.
		Assert.That(_replies.Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.DuplicateIdempotencyKey));

		release.TrySetResult();
		await first;
	}

	[Test]
	public async Task An_over_long_idempotency_key_is_refused()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));

		await dispatcher.DispatchAsync(
			Invoke(idempotencyKey: new string('k', ProtocolLimits.MaxIdempotencyKeyLength + 1)),
			Reply,
			CancellationToken.None);

		Assert.That(Single().Error!.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task A_new_session_forgets_remembered_results()
	{
		var handler = new TestCapabilityHandler("actions");
		using var dispatcher = TestSession.Dispatcher(handler);

		await dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);
		dispatcher.ResetIdempotency();
		await dispatcher.DispatchAsync(Invoke(idempotencyKey: "key"), Reply, CancellationToken.None);

		Assert.That(handler.Invocations, Is.EqualTo(2));
	}

	[Test]
	public async Task A_second_capability_kind_is_dispatched_without_the_transport_knowing_about_it()
	{
		// The extension point issue 413 relies on: a kind the dispatcher has never heard of, driven by
		// nothing but a handler registration.
		var handler = new TestCapabilityHandler(CapabilityKinds.Weather) { LocalIds = ["berlin"] };
		using var dispatcher = TestSession.Dispatcher(handler);

		await dispatcher.DispatchAsync(Invoke(kind: CapabilityKinds.Weather, localId: "berlin", operation: "read"),
			Reply,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(handler.Invocations, Is.EqualTo(1));
			Assert.That(Single().Error, Is.Null);
		});
	}
}
