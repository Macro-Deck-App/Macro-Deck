using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

/// <summary>
/// One full conformance run against the fixture as an in-process subject, computed at most once and
/// shared by every fixture that would otherwise repeat it: <c>WellBehavedInProcessSuiteTests</c>,
/// <c>ReportHonestyTests</c>, and the in-process third of <c>ReusabilityTests.SameCheckIdsAcrossAllThreeSubjectKinds</c>.
///
/// <para>
/// A fresh <see cref="MacroDeckTestHost" /> per check that needs its own (MDC0203, MDC0204, MDC0601-0604,
/// MDC0701, MDC0804) already makes one full run the most expensive thing in this project; three identical
/// runs against the same subject repeat the same 35 assertions for no new information, so this exists to
/// run it once and let every consumer read the same <see cref="ConformanceReport" />.
/// </para>
/// </summary>
internal static class SharedInProcessConformanceRun
{
	private static readonly SemaphoreSlim _gate = new(1, 1);
	private static ConformanceSubject? _subject;
	private static ConformanceReport? _report;

	/// <summary>The shared report - runs the suite on the first call, and returns the same result to every later one.</summary>
	public static async Task<ConformanceReport> GetReportAsync()
	{
		if (_report is { } cached)
		{
			return cached;
		}

		await _gate.WaitAsync().ConfigureAwait(false);

		try
		{
			if (_report is { } cachedAfterWait)
			{
				return cachedAfterWait;
			}

			// The composition sets no identity - it comes from manifest.json now, and an in-process
			// subject has no such file on disk to read. Declaring the fixture's real identity here
			// (rather than taking PluginTestManifest's own defaults) is what makes
			// PluginIdAndVersionComeFromTheSubject and ReusabilityTests' cross-subject-kind comparison
			// meaningful: the in-process, executable and artifact reports all describe the same plugin.
			_subject = ConformanceSubject.InProcess(builder => WellBehavedPluginComposition.Configure(builder),
				new PluginTestManifest(id: "app.macro-deck.well-behaved-test-plugin",
					name: "Well-Behaved Test Plugin",
					version: "1.0.0"));
			_report = await new ConformanceRunner().RunAsync(_subject).ConfigureAwait(false);
			return _report;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Releases the subject this run started, if any consumer ever actually triggered one. Called once,
	/// from this assembly's own <see cref="NUnit.Framework.SetUpFixtureAttribute" />, after every fixture
	/// that might still call <see cref="GetReportAsync" /> has had its turn.
	/// </summary>
	public static async Task DisposeAsync()
	{
		if (_subject is { } subject)
		{
			await subject.DisposeAsync().ConfigureAwait(false);
		}
	}
}
