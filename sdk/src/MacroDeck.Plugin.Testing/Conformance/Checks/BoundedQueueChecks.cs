using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC08xx - B8: bounded logging, event and variable queues. Forcing genuine queue pressure needs a plugin
// that logs a lot on demand, which no protocol rule compels an arbitrary third-party plugin to provide -
// MDC0801/MDC0802 search declared actions the same way the timeout checks do, and are Recommended for the
// same reason. MDC0803 needs no cooperation at all: whatever has already been logged during ordinary
// operation is fair game to check structurally. MDC0804 is a weaker, but fully generic, proxy for
// "event.publish while disconnected is not replayed": this suite cannot make an arbitrary plugin publish
// something spontaneously while disconnected (there is no invocation channel to ask it to, and neither the
// sample nor the misbehaving fixture has a background/timer-driven publisher), so it instead asserts the
// observable absence a replay would produce - a burst of published events immediately after reconnecting.
// MDC0805 mirrors MDC0505's own regression-guard reasoning, scoped to variables/get.

/// <summary>Genuinely exercised by this repository's own misbehaving fixture's "log-flood" action.</summary>
internal sealed class LoggingWhilePausedDoesNotBlockOrLoseTrafficCheck() : ConformanceCheckBase("MDC0801",
	"Logging while draining is paused does not block, and queued traffic is not silently lost after resuming",
	ConformanceCategory.BoundedQueues,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var localId in ConformanceCheckSupport.DeclaredActionLocalIds(context))
		{
			var before = context.Logs.Events.Count + context.Logs.Dropped;

			await context.Host.PauseDrainingAsync().ConfigureAwait(false);

			using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var outcome = await ConformanceCheckSupport
				.TryExecuteAsync(context, localId, new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);

			if (outcome is null)
			{
				await context.Host.ResumeDrainingAsync().ConfigureAwait(false);

				return ConformanceCheckResult.Fail(
					"An action still completes normally while draining is paused - capability.result is exempt from the pause.",
					$"Invoking the declared action '{localId}' produced no reply within 5 s while draining was paused.");
			}

			var duringPause = context.Logs.Events.Count + context.Logs.Dropped;

			if (duringPause <= before)
			{
				await context.Host.ResumeDrainingAsync().ConfigureAwait(false);
				continue;
			}

			await context.Host.ResumeDrainingAsync().ConfigureAwait(false);

			try
			{
				await Wait.UntilAsync(() => context.Logs.Events.Count + context.Logs.Dropped > before,
						TimeSpan.FromSeconds(5),
						because: "log traffic queued while paused never appeared after resuming")
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Fail(
					"Traffic queued while draining is paused appears after draining resumes, or is counted in Dropped - never simply lost.",
					"No new log events, and no increase in Dropped, were observed after resuming.");
			}

			return ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("action invoked", localId)]);
		}

		return ConformanceCheckResult.Skip("No declared action produced any observable log output.");
	}
}

/// <summary>Real violator: <c>MisbehaviorFlags.BypassLoggingPipeline</c> - an author's own sink bypasses <c>log.publish</c> entirely, so the trailing Error never arrives at all.</summary>
internal sealed class ErrorSurvivesFloodAndDroppedIsHonestCheck() : ConformanceCheckBase("MDC0802",
	"Under a logging flood while paused, a trailing Error still survives and Dropped is reported honestly",
	ConformanceCategory.BoundedQueues,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var localId in ConformanceCheckSupport.DeclaredActionLocalIds(context))
		{
			var errorsBefore = context.Logs.AtLeast(LogLevels.Error).Count;
			var eventsBefore = context.Logs.Events.Count;
			var droppedBefore = context.Logs.Dropped;

			await context.Host.PauseDrainingAsync().ConfigureAwait(false);

			using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			var outcome = await ConformanceCheckSupport
				.TryExecuteAsync(context, localId, new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);

			if (outcome is null)
			{
				await context.Host.ResumeDrainingAsync().ConfigureAwait(false);
				continue;
			}

			await context.Host.ResumeDrainingAsync().ConfigureAwait(false);

			try
			{
				await Wait.UntilAsync(() => context.Logs.AtLeast(LogLevels.Error).Count > errorsBefore,
						TimeSpan.FromSeconds(5),
						because: "a trailing Error logged while paused never appeared after resuming")
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				// No Error arrived - a failure only if this action actually flooded, because only then was
				// there something for a priority channel to preserve an Error through. Nothing here can
				// make an arbitrary plugin log an Error on demand, so an action that logs a handful of
				// lines and no Error has simply not exercised the property; failing it would mark every
				// well-behaved plugin non-conformant for an absence the check never gave it a way to
				// avoid, and an id authors learn to ignore is worse than one that is not shipped.
				// A batch's worth of traffic is the smallest burst that could plausibly evict anything,
				// which makes MaxLogEventsPerBatch the natural line rather than an invented threshold.
				var produced = context.Logs.Events.Count - eventsBefore + (context.Logs.Dropped - droppedBefore);

				if (produced < ProtocolLimits.MaxLogEventsPerBatch)
				{
					continue;
				}

				return ConformanceCheckResult.Fail(
					"An Error logged while draining is paused still reaches the host after draining resumes - the priority channel survives an informational flood.",
					$"The action produced {produced} log event(s) while draining was paused, but no Error-level " +
					"event arrived within 5 s of resuming.");
			}

			return ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("action invoked", localId),
				ConformanceCheckSupport.Observe("dropped", context.Logs.Dropped - droppedBefore)
			]);
		}

		return ConformanceCheckResult.Skip(
			"No declared action produced a batch's worth of log traffic while draining was paused, so there " +
			"was no flood for a trailing Error to have to survive.");
	}
}

/// <summary>Fully generic: whatever has already been logged during ordinary operation is enough to check structurally, no flood required.</summary>
internal sealed class LogEventsRespectFieldLimitsCheck() : ConformanceCheckBase("MDC0803",
	"Every collected log event respects the protocol's structural field limits",
	ConformanceCategory.BoundedQueues,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var events = context.Logs.Events;

		if (events.Count == 0)
		{
			return Task.FromResult(ConformanceCheckResult.Skip("No log events have been collected yet."));
		}

		foreach (var logEvent in events)
		{
			if (logEvent.Message.Length > ProtocolLimits.MaxLogMessageLength)
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"A log event's rendered message stays within MaxLogMessageLength ({ProtocolLimits.MaxLogMessageLength} characters).",
					$"One event's message is {logEvent.Message.Length} characters."));
			}

			if (logEvent.Properties.Count > ProtocolLimits.MaxLogPropertiesPerEvent)
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"A log event carries at most MaxLogPropertiesPerEvent ({ProtocolLimits.MaxLogPropertiesPerEvent}) properties.",
					$"One event carries {logEvent.Properties.Count}."));
			}

			foreach (var (key, value) in logEvent.Properties)
			{
				if (value.Length > ProtocolLimits.MaxLogPropertyValueLength)
				{
					return Task.FromResult(ConformanceCheckResult.Fail(
						$"A log property's value stays within MaxLogPropertyValueLength ({ProtocolLimits.MaxLogPropertyValueLength} characters).",
						$"Property '{key}' is {value.Length} characters."));
				}
			}

			if (logEvent.SourceContext is { } sourceContext &&
				sourceContext.Length > ProtocolLimits.MaxLogSourceContextLength)
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"A log event's SourceContext stays within MaxLogSourceContextLength ({ProtocolLimits.MaxLogSourceContextLength} characters).",
					$"One event's SourceContext is {sourceContext.Length} characters."));
			}

			var depth = ExceptionDepth(logEvent.Exception);

			if (depth > ProtocolLimits.MaxLogExceptionDepth)
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"A log event's exception chain stays within MaxLogExceptionDepth ({ProtocolLimits.MaxLogExceptionDepth}).",
					$"One event's exception chain is {depth} deep."));
			}
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("log events checked", events.Count)
		]));
	}

	private static int ExceptionDepth(LogExceptionDto? exception)
	{
		var depth = 0;

		for (var current = exception; current is not null; current = current.Inner)
		{
			depth++;
		}

		return depth;
	}
}

/// <summary>
/// A weaker, but fully generic, proxy for "event.publish while disconnected is not replayed on reconnect" -
/// see this file's own remarks on why the literal trigger cannot be constructed generically. Builds its own
/// host, for the same reason every other disconnect-driving check in this suite does.
/// </summary>
internal sealed class PublishedEventsAreNotReplayedOnReconnectCheck() : ConformanceCheckBase("MDC0804",
	"Reconnecting does not replay a burst of previously published events",
	ConformanceCategory.BoundedQueues,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		await using var host = await MacroDeckTestHost.StartAsync().ConfigureAwait(false);
		var handle = await context.Subject.StartAsync(host, cancellationToken: cancellationToken).ConfigureAwait(false);

		try
		{
			try
			{
				await host.WaitForSessionAsync().ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Inconclusive(
					"This subject never established a session against a freshly built host.");
			}

			// A settle window before the baseline too, not only before the post-reconnect comparison below:
			// IntegrationLifecycleHostedService runs InitializeAsync as fire-and-forget off the session's own
			// Connected event, so a session existing is no guarantee that a plugin's own one-time startup
			// publish (an initial weather/refresh event, say) has already landed. Racing that publish into
			// "after reconnect" instead of "before" would read as a false replay - the very failure mode this
			// check exists to catch, self-inflicted by measuring too early rather than by the subject.
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
			var beforeDisconnect = host.Events.Published.Count;

			await host.DisconnectAsync(ProtocolCloseCodes.QueueOverflow, cancellationToken).ConfigureAwait(false);

			try
			{
				await ConformanceCheckSupport
					.WaitPumpingClockAsync(handle.Clock, () => host.WaitForSessionAsync(TimeSpan.FromSeconds(15)))
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Fail("The subject reconnects after a non-fatal disconnect.",
					"No new session appeared within 15 s of disconnecting.");
			}

			// A settle window immediately after reconnecting - long enough to catch a replay burst if one
			// happened, short enough that legitimate new activity from the plugin's own timers is unlikely
			// to land inside it.
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
			var afterReconnect = host.Events.Published.Count;

			return afterReconnect == beforeDisconnect
				? ConformanceCheckResult.Pass()
				: ConformanceCheckResult.Fail("No event.publish traffic is replayed immediately on reconnect.",
					$"The published event count went from {beforeDisconnect} to {afterReconnect} within 1 s of reconnecting.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>Regression guard, scoped to <c>variables/get</c> - see <c>ConcurrencyBoundIsHonestCheck</c>'s own remarks.</summary>
internal sealed class VariableConcurrencyBoundIsHonestCheck() : ConformanceCheckBase("MDC0805",
	"A burst of variables/get invocations beyond MaxConcurrentInvocations never exceeds the reported in-flight bound",
	ConformanceCategory.BoundedQueues,
	ConformanceRequirement.Recommended,
	ConformancePrecondition.Session)
{
	private const int BurstMargin = 8;

	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var variable = context.Session!.Declared
			.FirstOrDefault(capability =>
				string.Equals(capability.Kind, CapabilityKinds.Variables, StringComparison.Ordinal));

		if (variable is null)
		{
			return ConformanceCheckResult.Skip("This subject does not declare the variables capability.");
		}

		var burstSize = ProtocolLimits.MaxConcurrentInvocations + BurstMargin;

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		var options = new CapabilityInvokeOptions { CancellationToken = safetyNet.Token };

		var burst = Task.WhenAll(Enumerable.Range(0, burstSize)
			.Select(_ => context.Session.Variables.GetAsync(variable.LocalId, options)));

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
				$"Every variables/get invocation in a burst of {burstSize} eventually completes.",
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
