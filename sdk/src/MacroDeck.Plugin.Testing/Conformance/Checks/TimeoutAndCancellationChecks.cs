using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC05xx - B5: timeout and cancellation compliance. Deadlines here are wall-clock by construction (see
// CapabilityInvokeOptions's own remarks), so exercising a genuine timeout or an in-flight cancel needs an
// operation slow enough to still be running when the check reacts to it - no protocol rule guarantees an
// arbitrary third-party plugin has one. MDC0502 and MDC0504 are therefore Recommended and search every
// declared action for one, skipping cleanly when none is slow enough; against this repository's own
// misbehaving fixture (which declares exactly such an action, "stall") both genuinely pass. MDC0501 and
// MDC0503 need no such cooperation and are Required. MDC0505's concurrency bound is a regression guard -
// PluginSessionConnection's own connection-scoped gate queues a burst rather than ever letting
// CapabilityDispatcher's fail-fast RATE_LIMITED path see one (see PluginSessionConnectionTests.
// Concurrency_is_capped_at_the_protocol_limit's own comment), so this check asserts only what holds either
// way: the bound is never exceeded and nothing in a burst ever hangs.

/// <summary>No constructible violator: <c>CapabilityDispatcher</c> enforces its single-reply guarantee with an
/// <c>Interlocked.Exchange</c> latch a handler cannot observe or bypass through the public <c>ICapabilityHandler</c>
/// contract, so this is a regression guard rather than something a misbehaving plugin can be built to violate.</summary>
internal sealed class ExactlyOneReplyPerInvocationCheck() : ConformanceCheckBase("MDC0501",
	"An invocation receives exactly one reply, never more",
	ConformanceCategory.TimeoutAndCancellation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var declared = context.Session!.Declared;

		if (declared.Count == 0)
		{
			return ConformanceCheckResult.Skip("This subject declares no capabilities to invoke.");
		}

		var first = declared[0];
		var operation = ConformanceCheckSupport.DescribeOperationFor(first.Kind);

		if (operation is null)
		{
			return ConformanceCheckResult.Inconclusive(
				$"No known describe operation for capability kind '{first.Kind}'.");
		}

		var outcome = await context.Session.InvokeAsync(first.Kind, first.LocalId, operation).ConfigureAwait(false);

		// A brief settle window: a second, spurious reply would arrive close behind the first, not
		// eventually - this asserts an absence, not something worth waiting out a deadline for.
		await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

		var replies = ConformanceCheckSupport.ReplyCount(context.Host, outcome.CorrelationId);

		return replies == 1
			? ConformanceCheckResult.Pass()
			: ConformanceCheckResult.Fail("Exactly one reply is ever sent for one invocation.",
				$"{replies} replies were recorded for this correlation.");
	}
}

/// <summary>Real violator: <c>MisbehaviorFlags.IgnoreCancellation</c> - an executor that never observes its token hangs past the deadline instead of replying TIMEOUT.</summary>
internal sealed class DeadlineProducesTimeoutCheck() : ConformanceCheckBase("MDC0502",
	"A deadline that elapses produces TIMEOUT, and nothing arrives afterward",
	ConformanceCategory.TimeoutAndCancellation,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var localId in ConformanceCheckSupport.DeclaredActionLocalIds(context))
		{
			using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(3));

			var outcome = await ConformanceCheckSupport.TryExecuteAsync(context,
					localId,
					new CapabilityInvokeOptions
						{ Timeout = TimeSpan.FromMilliseconds(300), CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);

			if (outcome is null)
			{
				return ConformanceCheckResult.Fail(
					"A reply - even a TIMEOUT - arrives within the deadline plus a grace period.",
					$"Invoking the declared action '{localId}' with a 300 ms deadline produced no reply within 3 s; the plugin appears to have hung.");
			}

			if (outcome.Succeeded ||
				!string.Equals(outcome.Error?.Code, ProtocolErrorCodes.Timeout, StringComparison.Ordinal))
			{
				// Too fast (or failed for an unrelated reason) to say anything about deadline enforcement -
				// try the next declared action.
				continue;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
			var replies = ConformanceCheckSupport.ReplyCount(context.Host, outcome.CorrelationId);

			return replies == 1
				? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("action invoked", localId)])
				: ConformanceCheckResult.Fail(
					"Exactly one reply (the TIMEOUT itself) is ever sent for a deadline that elapsed.",
					$"{replies} replies were recorded for this correlation.");
		}

		return ConformanceCheckResult.Skip(
			"No declared action ran long enough, under a 300 ms deadline, to observe deadline enforcement.");
	}
}

/// <summary>No constructible violator: <c>CancellationRules</c> is applied entirely inside <c>CapabilityDispatcher</c>, before a handler ever runs.</summary>
internal sealed class CancelForUnknownOrAnsweredCorrelationIsNoOpCheck() : ConformanceCheckBase("MDC0503",
	"Cancelling an unknown or already-answered correlation produces no message at all",
	ConformanceCategory.TimeoutAndCancellation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var unknownId = Guid.CreateVersion7().ToString();
		await context.Session!.CancelAsync(unknownId).ConfigureAwait(false);
		await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

		// From the plugin only: ProtocolMessageLog records both directions, so a raw WithCorrelationId
		// count would also count the capability.cancel this check itself just sent to the plugin under
		// this same id - the host's own outbound message, not anything the plugin said.
		var unknownReplies = ConformanceCheckSupport.MessagesFromPluginWithCorrelationId(context.Host, unknownId);

		if (unknownReplies > 0)
		{
			return ConformanceCheckResult.Fail(
				"Cancelling an unknown correlation produces no message at all from the plugin.",
				$"{unknownReplies} message(s) from the plugin were recorded for the unknown correlation id.");
		}

		var declared = context.Session.Declared;

		if (declared.Count == 0)
		{
			return ConformanceCheckResult.Skip(
				"This subject declares no capabilities to invoke - only the unknown-correlation half could be exercised.");
		}

		var first = declared[0];
		var operation = ConformanceCheckSupport.DescribeOperationFor(first.Kind);

		if (operation is null)
		{
			return ConformanceCheckResult.Inconclusive(
				$"No known describe operation for capability kind '{first.Kind}'.");
		}

		var outcome = await context.Session.InvokeAsync(first.Kind, first.LocalId, operation).ConfigureAwait(false);

		// Same measure as the unknown-correlation half above, and for the same reason: the host's own
		// capability.cancel it is about to send carries this same correlation id, so counting by
		// direction (not just by capability.result) is what keeps this a "the plugin said nothing more"
		// assertion rather than one that would tolerate the plugin sending something else instead.
		var repliesBefore
			= ConformanceCheckSupport.MessagesFromPluginWithCorrelationId(context.Host, outcome.CorrelationId);

		await context.Session.CancelAsync(outcome.CorrelationId).ConfigureAwait(false);
		await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

		var repliesAfter
			= ConformanceCheckSupport.MessagesFromPluginWithCorrelationId(context.Host, outcome.CorrelationId);

		return repliesAfter == repliesBefore
			? ConformanceCheckResult.Pass()
			: ConformanceCheckResult.Fail(
				"Cancelling an already-answered correlation produces no further message from the plugin.",
				$"The message count from the plugin for this correlation grew from {repliesBefore} to {repliesAfter} after cancelling it.");
	}
}

/// <summary>Uses the same declared-action search as <see cref="DeadlineProducesTimeoutCheck" />; genuinely exercised by this repository's own misbehaving fixture's "stall" action.</summary>
internal sealed class CancelInFlightProducesOneCancelledReplyCheck() : ConformanceCheckBase("MDC0504",
	"Cancelling an in-flight invocation produces exactly one cancelled reply",
	ConformanceCategory.TimeoutAndCancellation,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var localId in ConformanceCheckSupport.DeclaredActionLocalIds(context))
		{
			using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var before = context.Host.Messages.OfType(MessageTypes.CapabilityInvoke).Count;

			var invokeTask = context.Session!.Actions.ExecuteAsync(localId,
				options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token });

			try
			{
				await Wait.UntilAsync(() => context.Host.Messages.OfType(MessageTypes.CapabilityInvoke).Count > before,
						TimeSpan.FromSeconds(2),
						because: "the invoke was never sent")
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				continue;
			}

			var invokes = context.Host.Messages.OfType(MessageTypes.CapabilityInvoke);
			var correlationId = invokes[^1].Envelope.Id;
			await context.Session.CancelAsync(correlationId).ConfigureAwait(false);

			CapabilityInvocationOutcome outcome;

			try
			{
				outcome = await invokeTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				continue;
			}

			if (outcome.Succeeded ||
				!string.Equals(outcome.Error?.Code, ProtocolErrorCodes.Cancelled, StringComparison.Ordinal))
			{
				// Completed on its own (too fast) or failed for an unrelated reason - try the next action.
				continue;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
			var replies = ConformanceCheckSupport.ReplyCount(context.Host, correlationId);

			return replies == 1
				? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("action invoked", localId)])
				: ConformanceCheckResult.Fail(
					"Exactly one reply (the cancelled result itself) is ever sent for an invocation that was cancelled.",
					$"{replies} replies were recorded for this correlation.");
		}

		return ConformanceCheckResult.Skip(
			"No declared action stayed in flight long enough to be cancelled before it completed on its own.");
	}
}

/// <summary>Regression guard - see this file's own remarks on why a fail-fast RATE_LIMITED reply is not what a well-behaved SDK plugin actually produces under a burst.</summary>
internal sealed class ConcurrencyBoundIsHonestCheck() : ConformanceCheckBase("MDC0505",
	"A burst beyond MaxConcurrentInvocations never exceeds the reported in-flight bound, and every invocation completes",
	ConformanceCategory.TimeoutAndCancellation,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	private const int BurstMargin = 8;

	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var declared = context.Session!.Declared;

		if (declared.Count == 0)
		{
			return ConformanceCheckResult.Skip("This subject declares no capabilities to invoke.");
		}

		var first = declared[0];
		var operation = ConformanceCheckSupport.DescribeOperationFor(first.Kind);

		if (operation is null)
		{
			return ConformanceCheckResult.Inconclusive(
				$"No known describe operation for capability kind '{first.Kind}'.");
		}

		var burstSize = ProtocolLimits.MaxConcurrentInvocations + BurstMargin;

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var options = new CapabilityInvokeOptions { CancellationToken = safetyNet.Token };

		var burst = Task.WhenAll(Enumerable.Range(0, burstSize)
			.Select(_ => context.Session.InvokeAsync(first.Kind, first.LocalId, operation, options: options)));

		var maxObserved = 0;

		while (!burst.IsCompleted)
		{
			var report = await context.Plugin.ProbeHealthAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

			if (report.InFlightInvocations is { } inFlight)
			{
				maxObserved = Math.Max(maxObserved, inFlight);
			}

			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
		}

		try
		{
			await burst.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ConformanceCheckResult.Fail(
				$"Every invocation in a burst of {burstSize} (past the {ProtocolLimits.MaxConcurrentInvocations}-invocation limit) eventually completes.",
				"At least one invocation never completed within 20 s.");
		}

		return maxObserved > ProtocolLimits.MaxConcurrentInvocations
			? ConformanceCheckResult.Fail(
				$"/_macrodeck/diagnostics never reports more than {ProtocolLimits.MaxConcurrentInvocations} in-flight invocations.",
				$"Observed {maxObserved}.")
			: ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("burst size", burstSize),
				ConformanceCheckSupport.Observe("max in-flight observed", maxObserved)
			]);
	}
}
