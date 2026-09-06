using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC06xx - B6: disconnect and reconnect behaviour. Every check here builds its own host and its own
// instance of the subject through ConformanceContext.Subject, rather than disconnecting the ambient
// context.Session other checks in the same run still depend on: PluginSessionView.InvokeAsync sends
// through the specific PluginConnection it was constructed with, which a disconnect permanently closes -
// disrupting the shared ambient session here would silently break every check that runs after this
// category. QueueOverflow is used throughout as "a close code worth retrying" - it is in
// ProtocolCloseCodes but not in PluginSessionConnection.Classify's fatal set, so it reconnects without
// forcing a specific reason on the subject the way a made-up code might.

/// <summary>After a non-fatal close, the subject reconnects and readiness recovers.</summary>
internal sealed class ReconnectsAndBecomesReadyCheck() : ConformanceCheckBase("MDC0601",
	"After a non-fatal disconnect, the subject reconnects and becomes ready again",
	ConformanceCategory.DisconnectAndReconnect,
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

			var report = await ConformanceCheckSupport
				.WaitForHealthAsync(handle.Plugin, r => r.Ready, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

			return report.Ready
				? ConformanceCheckResult.Pass()
				: ConformanceCheckResult.Fail("/_macrodeck/ready returns to 200 after reconnecting.",
					"/_macrodeck/ready did not report ready within 10 s of the new session appearing.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>Reconnecting inside the resume window presents <c>resumeSessionId</c> and resumes the same session id.</summary>
internal sealed class ResumeKeepsTheSameSessionCheck() : ConformanceCheckBase("MDC0602",
	"Reconnecting inside the resume window presents resumeSessionId and resumes with the same session id",
	ConformanceCategory.DisconnectAndReconnect,
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
			PluginSessionView original;

			try
			{
				original = await host.WaitForSessionAsync().ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Inconclusive(
					"This subject never established a session against a freshly built host.");
			}

			var originalSessionId = original.SessionId;

			await host.DisconnectAsync(ProtocolCloseCodes.QueueOverflow, cancellationToken).ConfigureAwait(false);

			PluginSessionView resumed;

			try
			{
				resumed = await ConformanceCheckSupport
					.WaitPumpingClockAsync(handle.Clock, () => host.WaitForSessionAsync(TimeSpan.FromSeconds(15)))
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Fail("The subject reconnects within the resume window.",
					"No new session appeared within 15 s.");
			}

			if (!resumed.Resumed)
			{
				return ConformanceCheckResult.Fail(
					"Reconnecting inside the resume window resumes the prior session (Resumed == true).",
					"The new session reported Resumed == false.");
			}

			if (!string.Equals(resumed.SessionId, originalSessionId, StringComparison.Ordinal))
			{
				return ConformanceCheckResult.Fail(
					$"A resumed session keeps the original session id ('{originalSessionId}').",
					$"The resumed session reported id '{resumed.SessionId}'.");
			}

			var hellos = host.Messages.OfType(MessageTypes.SessionHello);
			var hello = hellos[^1];
			var payload = hello.Envelope.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

			return payload?.ResumeSessionId is { } presented &&
				string.Equals(presented, originalSessionId, StringComparison.Ordinal)
					? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("session id", resumed.SessionId)])
					: ConformanceCheckResult.Fail(
						$"The reconnecting session.hello presents resumeSessionId '{originalSessionId}'.",
						$"It presented '{payload?.ResumeSessionId ?? "(none)"}'.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>
/// Real violator: <c>MisbehaviorFlags.NonIdempotentInit</c>. Forces a non-resume reconnect deterministically
/// by shortening the host's own resume window rather than waiting out the real 60-second one - the literal
/// "session.goodbye then reconnect" trigger the issue describes is not something a running check can compel
/// a subject to do on its own, since that is the plugin's own shutdown sequence.
///
/// <para>
/// Readiness alone cannot tell resume-and-succeed apart from resume-and-one-integration-silently-failed:
/// <c>PluginConnectionState.IsReady</c> is exactly <c>Status == Connected</c>, independent of whether any
/// integration's <c>InitializeAsync</c> ever returns - <c>IntegrationLifecycleHostedService</c>'s own
/// remarks document this as deliberate ("a failing integration is logged and skipped rather than taking
/// the process down"). So a non-idempotent <c>InitializeAsync</c> that throws on the second call still
/// leaves the subject ready; the externally observable difference is the <c>Error</c>-level
/// "failed to initialize" log <c>UseMacroDeckLogging</c> ships for exactly this case. This check treats
/// readiness as a necessary baseline (the session and process are alive) and that log's absence as what
/// actually proves re-initialization succeeded.
/// </para>
/// </summary>
internal sealed class NonResumeReconnectReinitializesCheck() : ConformanceCheckBase("MDC0603",
	"A reconnect outside the resume window opens a fresh session and the subject becomes ready again after re-initializing",
	ConformanceCategory.DisconnectAndReconnect,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		// Zero, not merely short: ReconnectPolicy.DelayFor(1, jitterSample) can itself sample arbitrarily
		// close to zero, so any positive window - even a few milliseconds - risks a reconnect that
		// genuinely lands inside it on a fast (in-process, loopback) subject, making this a race rather
		// than a deterministic non-resume. Zero means "in the window" requires the resume attempt to land
		// at the exact same instant as the drop, which real socket and process overhead never does.
		var timeouts = ProtocolDescriptorFactory.CreateTimeoutsDescriptor() with
		{
			SessionResumeWindow = TimeSpan.Zero
		};

		await using var host = await MacroDeckTestHost.StartAsync(new MacroDeckTestHostOptions { Timeouts = timeouts })
			.ConfigureAwait(false);
		var handle = await context.Subject.StartAsync(host, cancellationToken: cancellationToken).ConfigureAwait(false);

		try
		{
			PluginSessionView original;

			try
			{
				original = await host.WaitForSessionAsync().ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Inconclusive(
					"This subject never established a session against a freshly built host.");
			}

			var originalSessionId = original.SessionId;

			// host.WaitForSessionAsync returns as soon as the *host's* side of the handshake completes -
			// nothing here waits for the subject's own connect-time InitializeAsync (which, for any
			// subject that reads config or otherwise calls back into the host on connect, is still an
			// in-flight host.invoke at that exact instant). Disconnecting immediately can therefore land
			// inside that call rather than after it: HostInvoker fails a call in flight when its
			// connection ends (#514), so the original init's own host.invoke throws and is logged as an
			// Error - one this check would otherwise misattribute to the *re-init* it disconnects to
			// provoke, since Error-level log delivery is itself batched and can arrive after the
			// disconnect. A brief settle window - generic, not sample-specific, since a third-party
			// subject's own connect-time work is opaque to this suite - is what keeps the two apart. An
			// in-process subject's near-zero latency makes this race very unlikely to land, which is why
			// it was never observed there.
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);

			var errorsBeforeReconnect = host.Logs.AtLeast(LogLevels.Error).Count;

			await host.DisconnectAsync(ProtocolCloseCodes.QueueOverflow, cancellationToken).ConfigureAwait(false);

			PluginSessionView fresh;

			try
			{
				fresh = await ConformanceCheckSupport
					.WaitPumpingClockAsync(handle.Clock, () => host.WaitForSessionAsync(TimeSpan.FromSeconds(15)))
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Fail("The subject reconnects even outside the resume window.",
					"No new session appeared within 15 s.");
			}

			if (fresh.Resumed)
			{
				return ConformanceCheckResult.Fail(
					"Reconnecting outside the resume window opens a fresh session (Resumed == false).",
					"The new session reported Resumed == true.");
			}

			if (string.Equals(fresh.SessionId, originalSessionId, StringComparison.Ordinal))
			{
				return ConformanceCheckResult.Fail("A fresh session is issued a new session id.",
					$"The new session kept the original id '{originalSessionId}'.");
			}

			var report = await ConformanceCheckSupport
				.WaitForHealthAsync(handle.Plugin, r => r.Ready, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

			if (!report.Ready)
			{
				return ConformanceCheckResult.Fail(
					"The subject becomes ready again once a fresh (non-resumed) reconnect finishes re-initializing its integrations.",
					"/_macrodeck/ready did not report ready within 15 s.");
			}

			// Readiness only proves the session came back, not that every integration's InitializeAsync
			// actually succeeded on it - see this type's own remarks. A failure there is isolated and
			// merely logged, so this waits up to 5 s (well past UseMacroDeckLogging's own 2 s default
			// flush interval) for that Error to arrive, the same way MDC0802 waits for one it expects.
			// Timing out here is the pass case: no news within the settle window is genuine good news.
			try
			{
				await Wait.UntilAsync(() => host.Logs.AtLeast(LogLevels.Error).Count > errorsBeforeReconnect,
						TimeSpan.FromSeconds(5),
						because: "an integration failed to re-initialize after the non-resumed reconnect")
					.ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				return ConformanceCheckResult.Pass([
					ConformanceCheckSupport.Observe("new session id", fresh.SessionId)
				]);
			}

			var newErrors = host.Logs.AtLeast(LogLevels.Error).Count - errorsBeforeReconnect;

			return ConformanceCheckResult.Fail(
				"Every integration re-initializes without error after a non-resumed reconnect.",
				$"{newErrors} new Error-level log event(s) appeared after reconnecting - a non-idempotent " +
				"InitializeAsync that throws on a second call produces exactly this symptom.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>
/// Close code 4004 (<see cref="ProtocolCloseCodes.SupervisorShutdown" />) is fatal for both registration
/// modes - only the "stop the process or not" decision that follows differs by mode, defaulting to stop
/// for a managed subject and stay up for a self-registering one; see
/// <c>PluginConnectionHostedService.Fault</c>'s own remarks. Which half this check exercises follows from
/// <see cref="ConformanceSubject.InProcess" />/<see cref="ConformanceSubject.Executable" />'s own documented
/// default credentials for the ambient subject.
/// </summary>
internal sealed class SupervisorShutdownStopsOnlyManagedSubjectsCheck() : ConformanceCheckBase("MDC0604",
	"A close of SupervisorShutdown (4004) stops a managed subject but leaves a self-registering one running",
	ConformanceCategory.DisconnectAndReconnect,
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

			var before = await handle.Plugin.ProbeHealthAsync().ConfigureAwait(false);
			var isManaged = string.Equals(before.Mode, "Managed", StringComparison.Ordinal);

			await host.DisconnectAsync(ProtocolCloseCodes.SupervisorShutdown, cancellationToken).ConfigureAwait(false);

			if (isManaged)
			{
				var report = await ConformanceCheckSupport
					.WaitForHealthAsync(handle.Plugin, r => !r.Live, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

				return !report.Live
					? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("mode", "Managed")])
					: ConformanceCheckResult.Fail(
						"A managed subject stops after the host closes its connection with SupervisorShutdown (4004).",
						"The subject was still live 15 s later.");
			}

			await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
			var stillLive = await handle.Plugin.ProbeHealthAsync().ConfigureAwait(false);

			return stillLive.Live
				? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("mode", "SelfRegistering")])
				: ConformanceCheckResult.Fail(
					"A self-registering subject stays up after the host closes its connection with SupervisorShutdown (4004).",
					"The subject was no longer live 3 s later.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}
