using System.Globalization;
using System.Net;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC07xx - B7: health endpoint behaviour. MDC0701 needs a session gate held from before the subject even
// starts, so - like MDC0603/MDC0604 - it builds its own host and its own instance through
// ConformanceContext.Subject rather than touching the ambient session. The rest read-only probe the
// ambient subject and are safe to share it.

/// <summary>The only state where liveness and readiness can disagree: before any session exists at all.</summary>
internal sealed class ReadyOnlyAfterSessionCheck() : ConformanceCheckBase("MDC0701",
	"/_macrodeck/health answers before any session exists; /_macrodeck/ready does not until one does",
	ConformanceCategory.HealthEndpoint,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var gate = PluginSessionGate.Held();

		await using var host = await MacroDeckTestHost
			.StartAsync(new MacroDeckTestHostOptions { SessionCreation = gate }).ConfigureAwait(false);
		var handle = await context.Subject.StartAsync(host, cancellationToken: cancellationToken).ConfigureAwait(false);

		try
		{
			var before = await ConformanceCheckSupport
				.WaitForHealthAsync(handle.Plugin, r => r.Live, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

			if (!before.Live)
			{
				return ConformanceCheckResult.Inconclusive(
					"The subject's /_macrodeck/health never answered while no session could exist yet.");
			}

			if (before.Ready)
			{
				return ConformanceCheckResult.Fail(
					"/_macrodeck/ready answers 503 while no session can exist yet (the session gate is held).",
					"/_macrodeck/ready reported ready before any session could have been created.");
			}

			gate.Release();

			// The gate's own refusal is a retryable 503 (see PluginSessionGate's remarks), so the plugin
			// is not failing to connect - it is backing off between retries on the same reconnect schedule
			// disconnect-driven checks pump; releasing the gate does nothing until that pending delay fires.
			var after = await ConformanceCheckSupport
				.WaitPumpingClockAsync(handle.Clock,
					() => ConformanceCheckSupport.WaitForHealthAsync(handle.Plugin,
						r => r.Ready,
						TimeSpan.FromSeconds(15)))
				.ConfigureAwait(false);

			return after.Ready
				? ConformanceCheckResult.Pass()
				: ConformanceCheckResult.Fail("/_macrodeck/ready answers 200 once a session is allowed to exist.",
					"/_macrodeck/ready did not report ready within 15 s of releasing the session gate.");
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>Cross-checks the ambient session's own diagnostics against what the host itself recorded - no probing needed, so this is safe to run against the shared ambient subject.</summary>
internal sealed class InfoAndDiagnosticsAgreeWithHostCheck() : ConformanceCheckBase("MDC0702",
	"/_macrodeck/info and /_macrodeck/diagnostics agree with what the host itself observed about this session",
	ConformanceCategory.HealthEndpoint,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var report = await context.Plugin.ProbeHealthAsync().ConfigureAwait(false);

		// Compared against what this session actually negotiated, not ProtocolVersions.Current: a
		// correctly-negotiating plugin pinned to an older version is not a health-endpoint failure, and
		// MDC0702 is about the two endpoints agreeing with the host's own record of this session, not
		// about every plugin running the newest protocol.
		if (report.NegotiatedVersion != context.Session!.NegotiatedVersion)
		{
			return ConformanceCheckResult.Fail(
				$"/_macrodeck/diagnostics reports negotiatedVersion {context.Session.NegotiatedVersion} - the version this session actually negotiated.",
				$"It reported {report.NegotiatedVersion?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}.");
		}

		if (report.DeclaredCapabilities != context.Session!.Declared.Count)
		{
			return ConformanceCheckResult.Fail(
				$"/_macrodeck/diagnostics reports {context.Session.Declared.Count} declared capabilities - the same count the host itself recorded.",
				$"It reported {report.DeclaredCapabilities?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}.");
		}

		var acceptedCount = context.Session.Accepted.Count(result => result.Accepted);

		if (report.AcceptedCapabilities != acceptedCount)
		{
			return ConformanceCheckResult.Fail(
				$"/_macrodeck/diagnostics reports {acceptedCount} accepted capabilities - the same count the host itself recorded.",
				$"It reported {report.AcceptedCapabilities?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}.");
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("id", report.Id),
			ConformanceCheckSupport.Observe("name", report.Name),
			ConformanceCheckSupport.Observe("version", report.Version),
			ConformanceCheckSupport.Observe("mode", report.Mode)
		]);
	}
}

/// <summary>Regression guard, exercised generically against any path under the reserved prefix the SDK does not itself serve.</summary>
internal sealed class UnknownReservedRouteIs404Check() : ConformanceCheckBase("MDC0703",
	"An unmapped route under /_macrodeck/ answers 404",
	ConformanceCategory.HealthEndpoint,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		using var client = new HttpClient();
		using var response = await client
			.GetAsync(new Uri(context.Plugin.BaseAddress, "/_macrodeck/not-a-real-endpoint"), cancellationToken)
			.ConfigureAwait(false);

		return response.StatusCode == HttpStatusCode.NotFound
			? ConformanceCheckResult.Pass()
			: ConformanceCheckResult.Fail("An unmapped path under /_macrodeck/ answers 404 Not Found.",
				$"It answered {(int)response.StatusCode} {response.StatusCode}.");
	}
}

/// <summary>
/// This check's own runtime assertion is deliberately thin: a subject that overrides <c>ASPNETCORE_URLS</c>
/// before building (the documented foot-gun - see the misbehaving fixture's self-set-urls flag) never
/// reaches a running <see cref="ConformanceContext" /> at all. <c>MacroDeckTestHost.LaunchAsync</c>/<c>HostAsync</c>
/// fail outright while probing the address the launcher itself reserved - exactly the observable symptom
/// in production too, a supervisor whose health probe never finds the process it just started. The
/// counterexample for this rule is exercised at the launch-failure level in this suite's own tests, not by
/// a check that, by definition, only ever runs once a session already exists.
/// </summary>
internal sealed class ListensWhereTheLauncherExpectsCheck() : ConformanceCheckBase("MDC0704",
	"The subject serves its endpoints at the base address its launcher was told to expect",
	ConformanceCategory.HealthEndpoint,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var report = await context.Plugin.ProbeHealthAsync().ConfigureAwait(false);

		return report.Live
			? ConformanceCheckResult.Pass()
			: ConformanceCheckResult.Fail(
				"The subject answers /_macrodeck/health at the base address this suite already used to establish a session with it.",
				"/_macrodeck/health did not answer.");
	}
}
