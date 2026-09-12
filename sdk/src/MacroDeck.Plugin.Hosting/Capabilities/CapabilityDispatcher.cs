using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Correlation;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities;

/// <summary>
/// Turns an inbound <c>capability.invoke</c> into a call on the handler that owns it, and guarantees
/// the one thing the protocol will not forgive: exactly one reply per invocation, whether it
/// completed, timed out, was cancelled or threw.
/// </summary>
internal sealed class CapabilityDispatcher(
	CapabilityCatalog catalog,
	IServiceScopeFactory scopeFactory,
	PluginConnectionState state,
	TimeProvider timeProvider,
	ILogger logger) : IDisposable
{
	private readonly ILogger _logger = logger.ForContext<CapabilityDispatcher>();

	private readonly ConcurrentDictionary<string, Invocation> _byCorrelation = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, IdempotentEntry> _byIdempotencyKey = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _concurrency = new(ProtocolLimits.MaxConcurrentInvocations);

	/// <summary>Releases the concurrency gate. The dispatcher lives as long as the plugin does.</summary>
	public void Dispose() => _concurrency.Dispose();

	/// <summary>Sends one envelope back to the host.</summary>
	internal delegate ValueTask Reply(ProtocolEnvelope envelope, CancellationToken cancellationToken);

	/// <summary>
	/// Runs an invocation and replies exactly once. Never throws: a failure inside a handler is a
	/// failed result, not a broken session.
	/// </summary>
	public async Task DispatchAsync(ProtocolEnvelope envelope, Reply reply, CancellationToken connectionToken)
	{
		var correlationId = envelope.Id;

		if (!TryBind(envelope, out var payload, out var bindingError))
		{
			await reply(Failure(correlationId, bindingError!), connectionToken);
			return;
		}

		if (envelope.IdempotencyKey is { Length: > ProtocolLimits.MaxIdempotencyKeyLength })
		{
			await reply(Failure(correlationId,
					Error(ProtocolErrorCodes.InvalidPayload,
						$"The idempotency key exceeds {ProtocolLimits.MaxIdempotencyKeyLength} characters.")),
				connectionToken);
			return;
		}

		if (TryReplayIdempotent(envelope.IdempotencyKey, out var replayed))
		{
			await reply(ToEnvelope(correlationId, replayed!), connectionToken);
			return;
		}

		if (!await _concurrency.WaitAsync(TimeSpan.Zero, connectionToken))
		{
			// Refused immediately rather than queued: the inbound channel is already the queue, and a
			// second one would only turn a rejection the caller could act on into a timeout it cannot.
			// The claim is released because this invocation never ran - the retry the caller is invited
			// to make must not come back as a duplicate.
			Forget(envelope.IdempotencyKey);

			await reply(Failure(correlationId,
					Error(ProtocolErrorCodes.RateLimited,
						$"This plugin is already running {ProtocolLimits.MaxConcurrentInvocations} invocations.",
						retryable: true)),
				connectionToken);
			return;
		}

		var invocation = new Invocation(correlationId,
			envelope.DeadlineMs,
			timeProvider.GetUtcNow(),
			connectionToken);
		_byCorrelation[correlationId] = invocation;
		state.InvocationStarted();

		try
		{
			var result = await RunAsync(payload!, envelope, invocation);

			// A run that finished after its cancel was answered stays cached, or a retry would run it twice.
			if (result.Error?.Code == ProtocolErrorCodes.Cancelled)
			{
				Forget(envelope.IdempotencyKey);
			}
			else
			{
				Remember(envelope.IdempotencyKey, result);
			}

			if (invocation.TryComplete())
			{
				await reply(ToEnvelope(correlationId, result), connectionToken);
			}
		}
		finally
		{
			_byCorrelation.TryRemove(correlationId, out _);
			state.InvocationFinished();
			invocation.Dispose();
			_concurrency.Release();
		}
	}

	/// <summary>
	/// Handles an inbound <c>capability.cancel</c>. Returns the result envelope to send, or null when
	/// the rules say to do nothing.
	/// </summary>
	public ProtocolEnvelope? Cancel(ProtocolEnvelope envelope)
	{
		var correlationId = envelope.CorrelationId;

		if (string.IsNullOrEmpty(correlationId))
		{
			return null;
		}

		var known = _byCorrelation.TryGetValue(correlationId, out var invocation);
		var outcome = CancellationRules.Resolve(known, alreadyReplied: invocation?.Replied ?? true);

		if (outcome != CancellationOutcome.EmitCancelledResult || invocation is null)
		{
			return null;
		}

		invocation.Cancel();

		// The running invocation observes cancellation and stops, but it is the cancel path that owns
		// the single reply from here: waiting for the handler to notice would let a handler that
		// ignores its token swallow the cancellation entirely.
		if (!invocation.TryComplete())
		{
			return null;
		}

		return Failure(correlationId,
			Error(ProtocolErrorCodes.Cancelled, ProtocolErrorMessages.For(ProtocolErrorCodes.Cancelled)));
	}

	/// <summary>
	/// Abandons everything in flight because the connection dropped. No results are emitted: the
	/// socket is gone, and after a resume the host has already given up on these correlations.
	/// </summary>
	public void AbortInFlight()
	{
		foreach (var invocation in _byCorrelation.Values)
		{
			invocation.TryComplete();
			invocation.Cancel();
		}

		_byCorrelation.Clear();
	}

	/// <summary>
	/// Forgets remembered results because a brand new session started. Deliberately not called on a
	/// resume: the host's idempotency cache survives a resume, so the plugin's has to as well, or a
	/// redelivery under a known key would re-execute.
	/// </summary>
	public void ResetIdempotency() => _byIdempotencyKey.Clear();

	private async Task<CapabilityInvocationResult> RunAsync(
		CapabilityInvokePayload payload,
		ProtocolEnvelope envelope,
		Invocation invocation)
	{
		var handler = catalog.Find(payload.Kind);

		if (handler is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"This plugin does not implement the '{payload.Kind}' capability.");
		}

		await using var scope = scopeFactory.CreateAsyncScope();

		var invocationContext = new CapabilityInvocationContext
		{
			Kind = payload.Kind,
			LocalId = payload.LocalId,
			Operation = payload.Operation,
			CorrelationId = envelope.Id,
			Deadline = invocation.Deadline,
			CancellationToken = invocation.Token
		};

		scope.ServiceProvider.GetRequiredService<CapabilityInvocationContextHolder>().Current = invocationContext;

		try
		{
			return await handler.InvokeAsync(new CapabilityInvocation
				{
					Kind = payload.Kind,
					LocalId = payload.LocalId,
					Operation = payload.Operation,
					Arguments = payload.Arguments,
					CorrelationId = envelope.Id,
					IdempotencyKey = envelope.IdempotencyKey,
					Deadline = invocation.Deadline,
					Services = scope.ServiceProvider
				},
				invocation.Token);
		}
		catch (OperationCanceledException) when (invocation.Token.IsCancellationRequested)
		{
			var timedOut = !invocation.CancelledByPeer;

			return CapabilityInvocationResult.Failed(
				timedOut ? ProtocolErrorCodes.Timeout : ProtocolErrorCodes.Cancelled,
				ProtocolErrorMessages.For(timedOut ? ProtocolErrorCodes.Timeout : ProtocolErrorCodes.Cancelled));
		}
		catch (Exception exception)
		{
			// A handler is third-party code by definition, so anything it throws has to become a
			// result rather than a torn-down session. The message is not passed through: an exception
			// message can carry a path, a query or a token, and this one is going over the wire.
			_logger.CapabilityThrew(payload.Kind, payload.LocalId, payload.Operation, exception);

			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InternalError,
				ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError));
		}
	}

	private bool TryReplayIdempotent(string? key, out CapabilityInvocationResult? result)
	{
		result = null;

		if (string.IsNullOrEmpty(key))
		{
			return false;
		}

		if (!_byIdempotencyKey.TryGetValue(key, out var entry))
		{
			// Claims the key so a concurrent repeat sees it as in flight. Losing the race is
			// indistinguishable from arriving second, which is what the contract describes.
			if (_byIdempotencyKey.TryAdd(key, IdempotentEntry.InFlight))
			{
				return false;
			}

			entry = _byIdempotencyKey[key];
		}

		if (entry.Result is null)
		{
			// In flight. The contract is explicit that this fails rather than joining the first
			// invocation, so the caller learns its retry was premature.
			result = CapabilityInvocationResult.Failed(ProtocolErrorCodes.DuplicateIdempotencyKey,
				ProtocolErrorMessages.For(ProtocolErrorCodes.DuplicateIdempotencyKey));
			return true;
		}

		result = entry.Result;
		return true;
	}

	private void Remember(string? key, CapabilityInvocationResult result)
	{
		if (!string.IsNullOrEmpty(key))
		{
			_byIdempotencyKey[key] = new IdempotentEntry(result);
		}
	}

	/// <summary>Releases a claimed key that produced no result or was cancelled, so a retry runs again.</summary>
	private void Forget(string? key)
	{
		if (!string.IsNullOrEmpty(key))
		{
			_byIdempotencyKey.TryRemove(key, out _);
		}
	}

	private static bool TryBind(
		ProtocolEnvelope envelope,
		out CapabilityInvokePayload? payload,
		out ProtocolError? error)
	{
		payload = null;
		error = null;

		if (envelope.Payload is not { } element)
		{
			error = Error(ProtocolErrorCodes.InvalidPayload, "capability.invoke requires a payload.");
			return false;
		}

		try
		{
			payload = element.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null || string.IsNullOrEmpty(payload.Kind) || string.IsNullOrEmpty(payload.LocalId))
		{
			error = Error(ProtocolErrorCodes.InvalidPayload,
				"capability.invoke requires 'kind', 'localId' and 'operation'.");
			return false;
		}

		return true;
	}

	private static ProtocolError Error(string code, string message, bool retryable = false)
		=> new() { Code = code, Message = Truncate(message), Retryable = retryable };

	private static string Truncate(string message)
		=> message.Length <= ProtocolLimits.MaxErrorMessageLength
			? message
			: message[..ProtocolLimits.MaxErrorMessageLength];

	private static ProtocolEnvelope ToEnvelope(string correlationId, CapabilityInvocationResult result)
		=> result.Error is { } error
			? Failure(correlationId, error)
			: new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityResult,
				Id = Guid.CreateVersion7().ToString(),
				CorrelationId = correlationId,
				Payload = JsonSerializer.SerializeToElement(new CapabilityResultPayload { Data = result.Data },
					PluginProtocolJson.Options)
			};

	private static ProtocolEnvelope Failure(string correlationId, ProtocolError error)
		=> new()
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = correlationId,
			Error = error with
			{
				Message = Truncate(error.Message),
				Details = ProtocolDiagnostics.Redact(error.Details)
			}
		};

	/// <summary>One running invocation, and the single-reply guard that makes it safe.</summary>
	private sealed class Invocation : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private int _cancelledByPeer;
		private int _replied;

		public Invocation(string correlationId, int? deadlineMs, DateTimeOffset now, CancellationToken connectionToken)
		{
			CorrelationId = correlationId;
			_cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);

			// The protocol's invoke timeout is the ceiling even when the caller asked for longer: a
			// deadline the host will not wait for only keeps this side busy.
			var budget = deadlineMs is > 0
				? TimeSpan.FromMilliseconds(Math.Min(deadlineMs.Value,
					ProtocolTimeouts.CapabilityInvoke.TotalMilliseconds))
				: ProtocolTimeouts.CapabilityInvoke;

			Deadline = now + budget;
			_cancellation.CancelAfter(budget);
		}

		public string CorrelationId { get; }

		public DateTimeOffset Deadline { get; }

		public CancellationToken Token => _cancellation.Token;

		public bool Replied => Volatile.Read(ref _replied) == 1;

		/// <summary>True when a <c>capability.cancel</c> caused the cancellation, rather than the deadline.</summary>
		public bool CancelledByPeer => Volatile.Read(ref _cancelledByPeer) == 1;

		/// <summary>Wins the right to send the one reply. Only the first caller gets true.</summary>
		public bool TryComplete() => Interlocked.Exchange(ref _replied, 1) == 0;

		public void Cancel()
		{
			Volatile.Write(ref _cancelledByPeer, 1);

			try
			{
				_cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The invocation finished between the lookup and here; nothing left to cancel.
			}
		}

		public void Dispose() => _cancellation.Dispose();
	}

	/// <summary>A remembered idempotent invocation: in flight while <see cref="Result" /> is null.</summary>
	private sealed record IdempotentEntry(CapabilityInvocationResult? Result)
	{
		public static IdempotentEntry InFlight { get; } = new((CapabilityInvocationResult?)null);
	}
}

/// <summary>
/// Carries the ambient invocation into the per-invocation scope. Registered scoped and filled in by
/// the dispatcher before the handler runs, so <see cref="ICapabilityInvocationContext" /> can be
/// injected anywhere inside the scope.
/// </summary>
internal sealed class CapabilityInvocationContextHolder
{
	public ICapabilityInvocationContext? Current { get; set; }
}
