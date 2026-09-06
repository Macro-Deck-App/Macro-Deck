using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC02xx - B2: registration and version negotiation. Per the design notes this stage started from: the
// handshake half of B2 is not violable by a .NET SDK subject at all (a plugin's own PluginConnectionHostedService
// checks the discovery endpoint's SupportedVersions against its own [Minimum, Current] range before it ever
// POSTs a session, so MacroDeckTestHost's literal 422 branch in HandleCreateSessionAsync is unreachable from
// a real SDK plugin - the mismatch is caught locally first, with the same observable effect). MDC0201,
// MDC0202 and MDC0203 are therefore regression guards: real, runtime assertions, but not something an
// SDK-conformant plugin could realistically get wrong on its own.

/// <summary>Regression guard: <c>session.welcome</c> is sent exactly once and matches what <c>session.hello</c> asserted.</summary>
internal sealed class HandshakeAssertsGrantedValuesCheck() : ConformanceCheckBase("MDC0201",
	"session.hello asserts the granted protocol version and session id, and session.welcome answers exactly once",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var hellos = context.Host.Messages.OfType(MessageTypes.SessionHello);

		if (hellos.Count == 0)
		{
			return Task.FromResult(ConformanceCheckResult.Fail(
				"A session.hello is recorded for the session this subject established.",
				"No session.hello was recorded."));
		}

		var hello = hellos[0].Envelope.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

		if (hello is null)
		{
			return Task.FromResult(ConformanceCheckResult.Fail("session.hello carries a readable payload.",
				"session.hello's payload could not be read."));
		}

		if (hello.ProtocolVersion != context.Session!.NegotiatedVersion)
		{
			return Task.FromResult(ConformanceCheckResult.Fail(
				$"session.hello asserts protocolVersion {context.Session.NegotiatedVersion} - the version this session already negotiated over HTTP.",
				$"session.hello carried protocolVersion {hello.ProtocolVersion}."));
		}

		if (!string.Equals(hello.SessionId, context.Session.SessionId, StringComparison.Ordinal))
		{
			return Task.FromResult(ConformanceCheckResult.Fail(
				$"session.hello asserts sessionId '{context.Session.SessionId}' - the id already issued over HTTP.",
				$"session.hello carried sessionId '{hello.SessionId}'."));
		}

		var welcomes = context.Host.Messages.OfType(MessageTypes.SessionWelcome);

		return Task.FromResult(welcomes.Count == 1
			? ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("negotiated version", context.Session.NegotiatedVersion),
				ConformanceCheckSupport.Observe("session id", hello.SessionId)
			])
			: ConformanceCheckResult.Fail(
				"Exactly one session.welcome is sent - the handshake asserts the granted values once and never re-negotiates.",
				$"{welcomes.Count} session.welcome messages were recorded."));
	}
}

/// <summary>Regression guard: the handshake completes inside the advertised handshake timeout.</summary>
internal sealed class HandshakeCompletesWithinTimeoutCheck() : ConformanceCheckBase("MDC0202",
	"The handshake completes within ProtocolTimeouts.Handshake",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var hellos = context.Host.Messages.OfType(MessageTypes.SessionHello);
		var welcomes = context.Host.Messages.OfType(MessageTypes.SessionWelcome);

		if (hellos.Count == 0 || welcomes.Count == 0)
		{
			return Task.FromResult(ConformanceCheckResult.Skip("No completed handshake was recorded to time."));
		}

		var elapsed = welcomes[0].RecordedAt - hellos[0].RecordedAt;

		return Task.FromResult(elapsed <= ProtocolTimeouts.Handshake
			? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("handshake duration", elapsed)])
			: ConformanceCheckResult.Fail($"session.welcome follows session.hello within {ProtocolTimeouts.Handshake}.",
				$"{elapsed} elapsed between them."));
	}
}

/// <summary>
/// Regression guard, exercised against a dedicated host this check builds itself: a protocol range this
/// subject cannot speak is fatal, not an endless retry loop. Constructed via the client-side pre-check
/// (a mismatched discovery response), not the literal 422 HTTP path - see this file's own remarks.
/// </summary>
internal sealed class IncompatibleProtocolVersionIsFatalCheck() : ConformanceCheckBase("MDC0203",
	"A host whose protocol range this subject cannot speak causes it to stop, not retry forever",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var incompatibleVersion = ProtocolVersions.Current + 1000;

		await using var host = await MacroDeckTestHost
			.StartAsync(new MacroDeckTestHostOptions { OfferedVersions = [incompatibleVersion] })
			.ConfigureAwait(false);

		ConformanceSubjectHandle handle;

		try
		{
			handle = await context.Subject.StartAsync(host, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (PluginProcessExitedException exited)
		{
			// The strongest possible form of the property under test: the subject recognised a host it
			// cannot speak to and stopped before it ever began serving, rather than starting up and then
			// retrying forever. There is no health endpoint left to sample, and none is needed - a clean
			// exit code is what distinguishes this from the crash a supervisor would restart.
			var exitCode = exited.Plugin.ExitCode;
			await exited.Plugin.DisposeAsync().ConfigureAwait(false);

			return exitCode == 0
				? ConformanceCheckResult.Pass([
					ConformanceCheckSupport.Observe("outcome", "exited cleanly before serving")
				])
				: ConformanceCheckResult.Fail(
					"A subject that cannot speak the host's protocol version stops cleanly, so a supervisor " +
					"reads it as a deliberate exit rather than a crash to restart.",
					$"The subject exited with code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "(unknown)"}.");
		}

		try
		{
			var report = await ConformanceCheckSupport
				.WaitForHealthAsync(handle.Plugin, IsStopped, TimeSpan.FromSeconds(10))
				.ConfigureAwait(false);

			if (!IsStopped(report))
			{
				return ConformanceCheckResult.Fail(
					"Connecting to a host whose only advertised protocol version this subject cannot speak eventually " +
					"stops retrying - diagnostics status becomes 'Faulted', or the process stops serving " +
					"altogether (a managed subject's documented default on a fatal protocol error).",
					$"After 10 s, the subject was still live and diagnostics status was " +
					$"'{report.Status ?? "(unreported)"}' - it still appears to be retrying.");
			}

			// A further real interval sampled repeatedly, to distinguish "genuinely stopped" from "merely
			// between two retries when first sampled": whichever of the two legitimate outcomes was
			// reached (Faulted and still live, or no longer serving at all) must hold at every sample, not
			// just the first - nothing in the SDK restarts a faulted or exited connection on its own, so
			// only an active retry loop still running would ever move status back to Connecting/Reconnecting.
			for (var sample = 1; sample <= 4; sample++)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
				var settled = await handle.Plugin.ProbeHealthAsync().ConfigureAwait(false);

				if (IsRetrying(settled))
				{
					return ConformanceCheckResult.Fail(
						"Once it stops over an incompatible protocol version, the subject does not resume retrying.",
						$"{sample * 500} ms later, diagnostics status had changed to '{settled.Status}' - the subject is retrying again.");
				}
			}

			return ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("outcome",
					report.Live ? $"stayed Faulted (status '{report.Status}')" : "stopped serving")
			]);
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Either legitimate outcome per this check's own remarks: a managed subject's documented default is
	/// to stop the whole process on a fatal protocol error (<c>PluginHostOptions.StopApplicationOnFatalProtocolError</c>),
	/// which this suite observes as the health endpoint going unreachable rather than as a status value -
	/// there is no diagnostics report left to read once the process has exited. A self-registering subject
	/// stays up and simply reports <c>Faulted</c> forever instead.
	/// </summary>
	private static bool IsStopped(PluginHealthReport report)
		=> !report.Live ||
			string.Equals(report.Status, "Faulted", StringComparison.Ordinal) ||
			string.Equals(report.Status, "Stopped", StringComparison.Ordinal);

	/// <summary>The only two statuses that mean an attempt is in flight or scheduled - see <c>PluginConnectionStatus</c>.</summary>
	private static bool IsRetrying(PluginHealthReport report)
		=> report.Live &&
			(string.Equals(report.Status, "Connecting", StringComparison.Ordinal) ||
				string.Equals(report.Status, "Reconnecting", StringComparison.Ordinal));
}

/// <summary>
/// Exercised only against an in-process (and so, by this suite's own convention, self-registering) subject -
/// see <see cref="ConformanceSubject.InProcess" />'s remarks. Skips for an executable or artifact subject,
/// which always connects <see cref="PluginTestCredentials.Managed" /> and has nothing to persist.
/// </summary>
internal sealed class SelfRegistrationPersistsAcrossRestartsCheck() : ConformanceCheckBase("MDC0204",
	"A self-registering subject registers once and reuses its persisted credentials on a later start",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Plugin is not InProcessPlugin)
		{
			return ConformanceCheckResult.Skip(
				"This subject does not connect as a self-registering in-process plugin; state-directory sharing " +
				"across two starts is not observable for an executable or artifact subject through this suite.");
		}

		using var stateDirectory = new TempStateDirectory();
		await using var host = await MacroDeckTestHost.StartAsync().ConfigureAwait(false);

		var first = await context.Subject.StartAsync(host, stateDirectory.Path, cancellationToken)
			.ConfigureAwait(false);

		try
		{
			await host.WaitForSessionAsync().ConfigureAwait(false);
		}
		finally
		{
			await first.Plugin.DisposeAsync().ConfigureAwait(false);
		}

		var second = await context.Subject.StartAsync(host, stateDirectory.Path, cancellationToken)
			.ConfigureAwait(false);

		try
		{
			await host.WaitForSessionAsync().ConfigureAwait(false);
		}
		finally
		{
			await second.Plugin.DisposeAsync().ConfigureAwait(false);
		}

		return host.Registrations.Count == 1
			? ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("registrations across two starts", host.Registrations.Count)
			])
			: ConformanceCheckResult.Fail(
				"Exactly one registration is recorded across two starts sharing the same state directory - the " +
				"second reuses the credentials the first one persisted.",
				$"{host.Registrations.Count} registrations were recorded.");
	}
}

/// <summary>
/// Exercised only against an in-process subject, for the same reason <see cref="SelfRegistrationPersistsAcrossRestartsCheck" />
/// is: pairing is how a self-registering plugin with no enrollment token obtains its credential, and only
/// <see cref="ConformanceSubject.InProcess" /> ever runs without one. Starts its own instances with
/// <see cref="PluginTestCredentials.Pairing" /> rather than sharing the ambient session (which always uses
/// <see cref="PluginTestCredentials.SelfRegistering" />'s enrollment token instead, by design - see that
/// credential's own remarks on why an automated harness must never wait on a human by default).
/// </summary>
internal sealed class InteractivePairingObtainsAndPersistsCredentialsCheck() : ConformanceCheckBase("MDC0206",
	"A self-registering subject with no enrollment token pairs interactively - proving possession of its " +
	"own verifier rather than sending it twice - and reuses the persisted credential on a later start " +
	"without pairing again",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Plugin is not InProcessPlugin)
		{
			return ConformanceCheckResult.Skip(
				"This subject does not connect as a self-registering in-process plugin; interactive pairing is " +
				"not observable for an executable or artifact subject through this suite.");
		}

		using var stateDirectory = new TempStateDirectory();
		await using var host = await MacroDeckTestHost.StartAsync().ConfigureAwait(false);

		var first = await context.Subject
			.StartAsync(host, stateDirectory.Path, PluginTestCredentials.Pairing, cancellationToken)
			.ConfigureAwait(false);

		try
		{
			await host.WaitForSessionAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		catch (PluginTestTimeoutException)
		{
			return ConformanceCheckResult.Fail(
				"A self-registering subject with no enrollment token, and pairing enabled and supported by the " +
				"host, obtains a working session through interactive pairing.",
				"No session completed its handshake before the deadline while pairing was available.");
		}
		finally
		{
			await first.Plugin.DisposeAsync().ConfigureAwait(false);
		}

		if (host.PairingRequests.Count != 1 || host.PairingRedemptions.Count != 1)
		{
			return ConformanceCheckResult.Fail(
				"Exactly one pairing request is created and exactly one redemption attempted to obtain the first credential.",
				$"{host.PairingRequests.Count} pairing request(s) and {host.PairingRedemptions.Count} redemption attempt(s) were recorded.");
		}

		var challenge = host.PairingRequests.Single().CodeChallenge;
		var verifier = host.PairingRedemptions.Single().CodeVerifier;

		if (string.Equals(challenge, verifier, StringComparison.Ordinal))
		{
			return ConformanceCheckResult.Fail(
				"The code challenge sent when the pairing request is created is not the verifier redeemed with it " +
				"- sending the same value twice would prove nothing about possession.",
				"The challenge and the verifier were identical.");
		}

		var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

		if (!string.Equals(expectedChallenge, challenge, StringComparison.Ordinal))
		{
			return ConformanceCheckResult.Fail(
				"base64url(SHA-256(verifier)) equals the challenge sent when the pairing request was created - " +
				"proof of possession, PKCE-style.",
				"The redeemed verifier's hash did not match the original challenge.");
		}

		var second = await context.Subject
			.StartAsync(host, stateDirectory.Path, PluginTestCredentials.Pairing, cancellationToken)
			.ConfigureAwait(false);

		try
		{
			await host.WaitForSessionAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		catch (PluginTestTimeoutException)
		{
			return ConformanceCheckResult.Fail(
				"A second start against the same state directory reuses the credential pairing persisted and " +
				"reaches a working session without pairing again.",
				"No session completed its handshake before the deadline on the second start.");
		}
		finally
		{
			await second.Plugin.DisposeAsync().ConfigureAwait(false);
		}

		if (host.PairingRequests.Count != 1)
		{
			return ConformanceCheckResult.Fail(
				"A second start against the same state directory reuses the credential pairing already persisted, " +
				"rather than creating a second pairing request.",
				$"{host.PairingRequests.Count} pairing requests were recorded across two starts.");
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("pairing requests across two starts", host.PairingRequests.Count),
			ConformanceCheckSupport.Observe("registrations across two starts", host.Registrations.Count)
		]);
	}
}

/// <summary>Exercised only against a managed (executable or artifact) subject - the ambient in-process subject always self-registers.</summary>
internal sealed class ManagedSubjectNeverRegistersCheck() : ConformanceCheckBase("MDC0205",
	"A managed subject never calls the registration endpoint",
	ConformanceCategory.RegistrationAndNegotiation,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Plugin is not ExternalPlugin)
		{
			return Task.FromResult(
				ConformanceCheckResult.Skip("This subject does not connect as a managed, externally launched plugin."));
		}

		return Task.FromResult(context.Host.Registrations.IsEmpty
			? ConformanceCheckResult.Pass()
			: ConformanceCheckResult.Fail(
				"A managed subject never calls POST /api/plugins/registration - its id and secret are already supplied by its launcher.",
				$"{context.Host.Registrations.Count} registration attempts were recorded."));
	}
}
