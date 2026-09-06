using System.Diagnostics;
using MacroDeck.Plugin.Testing.Conformance.Checks;

namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// Runs every selected <see cref="IConformanceCheck" /> against one <see cref="ConformanceSubject" /> and
/// produces a <see cref="ConformanceReport" />.
///
/// <para>
/// Establishes one ambient <see cref="MacroDeckTestHost" /> and one ambient session for the whole run -
/// what <see cref="ConformanceContext.Host" />/<see cref="ConformanceContext.Plugin" />/<see cref="ConformanceContext.Session" />
/// expose to every check. A subject that never even starts (an artifact that fails to extract, a process
/// that never begins serving) is a launch failure this method deliberately lets propagate rather than
/// paper over with an empty report - there is no <see cref="ConformanceContext" /> to build without a
/// running <see cref="PluginUnderTest" /> in hand. A subject that starts but never completes a handshake is
/// different and is handled gracefully: every check declaring <see cref="ConformancePrecondition.Session" />
/// in <see cref="IConformanceCheck.Requires" /> is simply skipped.
/// </para>
/// </summary>
public sealed class ConformanceRunner
{
	/// <summary>
	/// This check suite's own version - bumped whenever the check set this runner executes changes, an
	/// existing check's meaning changes, or a passing subject could newly fail (or vice versa): the
	/// <see cref="Checks" /> guarantee is stable only within one version, so a third-party CI job pinned to
	/// a version must see the same set and the same verdicts from it every time. Never bumped for a purely
	/// cosmetic change, such as reworded <see cref="IConformanceCheck.Title" /> text. See
	/// <see cref="IConformanceCheck.Id" />'s own remarks on why an id is a stable contract independent of this.
	/// </summary>
	public const string SuiteVersion = "1.2.0";

	private readonly ConformanceOptions _options;

	/// <summary>Builds a runner. <paramref name="options" /> defaults to every check, unfiltered, with the default per-check timeout.</summary>
	public ConformanceRunner(ConformanceOptions? options = null)
	{
		_options = options ?? new ConformanceOptions();
		Checks = [.. AllChecks().Where(_options.Selects).OrderBy(check => check.Id, StringComparer.Ordinal)];
	}

	/// <summary>
	/// Every check this runner will execute, in stable <see cref="IConformanceCheck.Id" /> order, after
	/// <see cref="ConformanceOptions" /> filtering. Stable across runs of the same suite version: a
	/// third-party CI job iterating this list (as this repository's own NUnit adapter does, one test per
	/// entry) sees the same set for the same options every time.
	/// </summary>
	public IReadOnlyList<IConformanceCheck> Checks { get; }

	/// <summary>Runs every check in <see cref="Checks" /> against <paramref name="subject" /> and returns the resulting report.</summary>
	/// <exception cref="Exception">
	/// The subject never started at all (an artifact failed to extract, a process never began serving its
	/// own health endpoint). Propagated rather than reported: there is no running subject to build a
	/// <see cref="ConformanceContext" /> around.
	/// </exception>
	public async Task<ConformanceReport> RunAsync(ConformanceSubject subject,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(subject);

		var startedAt = DateTimeOffset.UtcNow;
		var stopwatch = Stopwatch.StartNew();

		await using var host = await MacroDeckTestHost.StartAsync().ConfigureAwait(false);
		var handle = await subject.StartAsync(host, cancellationToken: cancellationToken).ConfigureAwait(false);

		try
		{
			PluginSessionView? session = null;

			try
			{
				session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
			}
			catch (PluginTestTimeoutException)
			{
				// Left null: every Session-requiring check skips below rather than throwing, and the report
				// still enumerates every selected check - see this type's own remarks.
			}

			var healthReport = await handle.Plugin.ProbeHealthAsync().ConfigureAwait(false);

			var context = new ConformanceContext(subject,
				host,
				handle.Plugin,
				session,
				handle.Manifest,
				handle.Clock,
				_options);
			var results = new List<ConformanceCheckOutcome>(Checks.Count);

			foreach (var check in Checks)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var result = await RunOneAsync(check, context, cancellationToken).ConfigureAwait(false);

				results.Add(new ConformanceCheckOutcome
				{
					Id = check.Id,
					Title = check.Title,
					Category = check.Category,
					Requirement = check.Requirement,
					Result = result
				});
			}

			stopwatch.Stop();

			var passed = results.Count(result => result.Result.Outcome == ConformanceOutcome.Passed);
			var failed = results.Count(result => result.Result.Outcome == ConformanceOutcome.Failed);
			var skipped = results.Count - passed - failed;

			var conformant = results.All(result
				=> result.Requirement != ConformanceRequirement.Required ||
				result.Result.Outcome != ConformanceOutcome.Failed);

			return new ConformanceReport
			{
				SuiteVersion = SuiteVersion,
				PluginId = healthReport.Id,
				PluginVersion = healthReport.Version,
				StartedAt = startedAt,
				Duration = stopwatch.Elapsed,
				Results = results,
				Passed = passed,
				Failed = failed,
				Skipped = skipped,
				Conformant = conformant
			};
		}
		finally
		{
			await handle.Plugin.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Runs <paramref name="check" /> under two independent bounds: <paramref name="context" />'s own
	/// <see cref="ConformanceOptions.PerCheckTimeout" />, passed to the check as a linked
	/// <see cref="CancellationToken" /> so a well-behaved check can wind itself down; and a second,
	/// unconditional wall-clock race (<see cref="Task.WhenAny(Task, Task)" /> against a plain
	/// <see cref="Task.Delay(TimeSpan, CancellationToken)" />) that does not depend on the check ever
	/// observing that token at all. The second bound is what makes this resilient to a check with a bug
	/// exactly like the ones this suite exists to catch in a *subject* - one that ignores cancellation
	/// entirely: without it, a single wedged check would hang the whole run, no matter how carefully the
	/// token was threaded through <see cref="IConformanceCheck.RunAsync" />'s own implementation.
	/// </summary>
	private static async Task<ConformanceCheckResult> RunOneAsync(IConformanceCheck check,
		ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (UnmetPrecondition(check, context) is { } reason)
		{
			return ConformanceCheckResult.Skip(reason);
		}

		var perCheckTimeout = context.Options.PerCheckTimeout;
		var stopwatch = Stopwatch.StartNew();

		var timeoutSource = new CancellationTokenSource(perCheckTimeout);
		var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

		var checkTask = RunCatchingAsync(check, context, timeoutSource, linked.Token, cancellationToken);

		// A plain Task.Delay - never one of the TimeProvider-bound overloads - so this bound holds even
		// when context.Clock is a ManualTimeProvider a check itself (or a subject's own reconnect logic)
		// never advances.
		var watchdog = Task.Delay(perCheckTimeout, cancellationToken);

		if (await Task.WhenAny(checkTask, watchdog).ConfigureAwait(false) != checkTask)
		{
			stopwatch.Stop();

			// The watchdog itself only ever completes by the same cancellationToken firing (there is no
			// other way for Task.Delay to finish here besides its due time, which is what "the watchdog
			// won" already established) - so a genuine caller cancellation surfaces as a real
			// OperationCanceledException, exactly as it would have before this bound existed, rather than
			// being swallowed into an Inconclusive result.
			cancellationToken.ThrowIfCancellationRequested();

			// checkTask is abandoned, not awaited: it may never complete at all (that is the failure mode
			// this bound exists for). Observed so its eventual fault or cancellation never surfaces as an
			// unobserved task exception once it does resolve.
			_ = checkTask.ContinueWith(static t => _ = t.Exception,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

			return ConformanceCheckResult
				.Inconclusive($"The check did not return within its {perCheckTimeout} per-check timeout " +
					$"and was abandoned after {stopwatch.Elapsed} of real time.")
				.WithDuration(stopwatch.Elapsed);
		}

		timeoutSource.Dispose();
		linked.Dispose();
		stopwatch.Stop();
		return (await checkTask.ConfigureAwait(false)).WithDuration(stopwatch.Elapsed);
	}

	private static async Task<ConformanceCheckResult> RunCatchingAsync(
		IConformanceCheck check,
		ConformanceContext context,
		CancellationTokenSource timeoutSource,
		CancellationToken linkedToken,
		CancellationToken cancellationToken)
	{
		try
		{
			return await check.RunAsync(context, linkedToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested &&
			!cancellationToken.IsCancellationRequested)
		{
			// The check itself observed the per-check timeout and unwound cleanly - still no verdict on
			// the subject, so this is Inconclusive rather than Failed for the same reason the abandonment
			// branch in RunOneAsync is: the suite's own budget elapsing says nothing about which of this
			// check's assertions the subject would have passed or failed.
			return ConformanceCheckResult.Inconclusive(
				$"The check did not return within its {context.Options.PerCheckTimeout} per-check timeout.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// A check that throws is still a check the report must account for - see this type's own
			// remarks on why a subject is never silently dropped from Results. Failed, not Inconclusive: an
			// unexpected exception most often means the subject did something the check's own happy path
			// did not anticipate, which is itself evidence against conformance.
			return ConformanceCheckResult
				.Fail("The check completes without throwing.",
					$"The check threw {exception.GetType().Name}: {exception.Message}");
		}
	}

	private static string? UnmetPrecondition(IConformanceCheck check, ConformanceContext context)
	{
		foreach (var precondition in check.Requires)
		{
			var reason = precondition switch
			{
				ConformancePrecondition.Session when context.Session is null
					=> "This subject never established a session.",
				ConformancePrecondition.Manifest when context.Manifest is null
					=> "This subject has no manifest - only an artifact subject does.",
				ConformancePrecondition.ExternalProcess when context.Plugin is not ExternalPlugin
					=> "This subject is not a real, separate process.",
				ConformancePrecondition.Clock when context.Clock is null
					=> "This subject was not given a ManualTimeProvider - only an in-process subject is.",
				_ => null
			};

			if (reason is not null)
			{
				return reason;
			}
		}

		return null;
	}

	private static IEnumerable<IConformanceCheck> AllChecks()
	{
		// MDC01xx - manifest and identifier rules.
		yield return new PluginIdIsValidCheck();
		yield return new DeclaredLocalIdsAreValidCheck();
		yield return new RuntimeInstanceIdsAreValidCheck();
		yield return new ArtifactManifestIsWellFormedCheck();
		yield return new ManifestIdMatchesReportedIdCheck();
		yield return new ManifestNameAndVersionAreReportedCheck();
		yield return new ManifestIconMediaTypeIsReportedCheck();

		// MDC02xx - registration and version negotiation.
		yield return new HandshakeAssertsGrantedValuesCheck();
		yield return new HandshakeCompletesWithinTimeoutCheck();
		yield return new IncompatibleProtocolVersionIsFatalCheck();
		yield return new SelfRegistrationPersistsAcrossRestartsCheck();
		yield return new InteractivePairingObtainsAndPersistsCredentialsCheck();
		yield return new ManagedSubjectNeverRegistersCheck();

		// MDC03xx - capability serialization.
		yield return new DeclaredKindsAreKnownCheck();
		yield return new VersionRangesAreValidAndAcceptedCheck();
		yield return new ProviderShapedKindsDeclareOneCapabilityCheck();
		yield return new DeclaredCapabilityCountIsWithinLimitCheck();
		yield return new DescribeResultsRespectMessageSizeCheck();
		yield return new UiDescribeResultIsWellFormedCheck();
		yield return new UiDeclaredSessionModesAreNonEmptyCheck();
		yield return new UiDescribeResultRespectsMessageSizeCheck();
		yield return new ActionStateSnapshotsAreWellFormedCheck();
		yield return new ActionStateIdsAreValidDeclaredIdsCheck();
		yield return new VariableCatalogDiscoverPageIsWellFormedCheck();
		yield return new ActionIconSnapshotsAreWellFormedCheck();
		yield return new ActionIconContentIsAnswerableCheck();
		yield return new WritableVariablesAcceptWritesCheck();
		yield return new EagerVariableSetIsWithinLimitCheck();

		// MDC04xx - duplicate ids.
		yield return new NoDuplicateDeclaredIdsCheck();
		yield return new NoDuplicateProviderInstanceIdsCheck();
		yield return new NoDuplicateVariableDefinitionIdsCheck();

		// MDC05xx - timeout and cancellation compliance.
		yield return new ExactlyOneReplyPerInvocationCheck();
		yield return new DeadlineProducesTimeoutCheck();
		yield return new CancelForUnknownOrAnsweredCorrelationIsNoOpCheck();
		yield return new CancelInFlightProducesOneCancelledReplyCheck();
		yield return new ConcurrencyBoundIsHonestCheck();

		// MDC06xx - disconnect and reconnect behaviour.
		yield return new ReconnectsAndBecomesReadyCheck();
		yield return new ResumeKeepsTheSameSessionCheck();
		yield return new NonResumeReconnectReinitializesCheck();
		yield return new SupervisorShutdownStopsOnlyManagedSubjectsCheck();

		// MDC07xx - health endpoint behaviour.
		yield return new ReadyOnlyAfterSessionCheck();
		yield return new InfoAndDiagnosticsAgreeWithHostCheck();
		yield return new UnknownReservedRouteIs404Check();
		yield return new ListensWhereTheLauncherExpectsCheck();

		// MDC08xx - bounded logging, event and variable queues.
		yield return new LoggingWhilePausedDoesNotBlockOrLoseTrafficCheck();
		yield return new ErrorSurvivesFloodAndDroppedIsHonestCheck();
		yield return new LogEventsRespectFieldLimitsCheck();
		yield return new PublishedEventsAreNotReplayedOnReconnectCheck();
		yield return new VariableConcurrencyBoundIsHonestCheck();
	}
}
