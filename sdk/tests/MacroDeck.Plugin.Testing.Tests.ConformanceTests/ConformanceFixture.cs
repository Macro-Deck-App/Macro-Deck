using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// Runs the full conformance suite against one subject, once, in <see cref="RunSuiteAsync" />, and exposes
/// the result to a <see cref="TestCaseSourceAttribute" />-driven test per check. A check added to
/// <c>MacroDeck.Plugin.Testing.Conformance</c> becomes a new test under every fixture derived from this one
/// automatically - <see cref="Checks" /> is evaluated at test discovery time, well before
/// <see cref="RunSuiteAsync" /> ever runs, which is why it enumerates <see cref="IConformanceCheck" />
/// instances rather than results: the per-check test method looks its own result up from
/// <see cref="Report" /> once the suite has actually run.
/// </summary>
internal abstract class ConformanceFixture
{
	private static readonly ConformanceRunner _runner = new();

	private ConformanceSubject? _subject;
	private ConformanceReport? _report;

	/// <summary>Every check this suite runs, in stable order - the source for <see cref="CheckOutcomeIsAcceptable" />.</summary>
	public static IReadOnlyList<IConformanceCheck> Checks => _runner.Checks;

	/// <summary>
	/// Builds the subject this fixture exercises, when <see cref="GetReportAsync" /> is not itself
	/// overridden. Called once, from the default <see cref="GetReportAsync" />, and the subject it
	/// returns is disposed once by <see cref="DisposeSubjectAsync" />.
	/// </summary>
	protected virtual Task<ConformanceSubject> CreateSubjectAsync()
		=> throw new NotSupportedException(
			$"{GetType().Name} must override either {nameof(CreateSubjectAsync)} or {nameof(GetReportAsync)}.");

	/// <summary>The completed report. Throws if accessed before <see cref="RunSuiteAsync" /> has run.</summary>
	protected ConformanceReport Report =>
		_report ?? throw new InvalidOperationException("The conformance suite has not run yet.");

	[OneTimeSetUp]
	public async Task RunSuiteAsync() => _report = await GetReportAsync();

	/// <summary>
	/// Produces the report <see cref="Report" /> exposes. The default builds a fresh subject through
	/// <see cref="CreateSubjectAsync" /> and runs the full suite against it once, remembering the subject
	/// so <see cref="DisposeSubjectAsync" /> can release it afterwards. Override to reuse a report a
	/// shared run already computed instead of repeating the same 35 assertions against the same subject -
	/// see <c>WellBehavedInProcessSuiteTests</c> and <c>Support.SharedInProcessConformanceRun</c>.
	/// </summary>
	protected virtual async Task<ConformanceReport> GetReportAsync()
	{
		_subject = await CreateSubjectAsync();
		return await _runner.RunAsync(_subject);
	}

	[OneTimeTearDown]
	public async Task DisposeSubjectAsync()
	{
		if (_subject is not null)
		{
			await _subject.DisposeAsync();
		}
	}

	/// <summary>
	/// A <see cref="ConformanceOutcome.Failed" /> outcome on a <see cref="ConformanceRequirement.Required" />
	/// check fails this test; on a <see cref="ConformanceRequirement.Recommended" /> one it only warns -
	/// mirroring exactly what does and does not affect <see cref="ConformanceReport.Conformant" />. A
	/// <see cref="ConformanceOutcome.Skipped" /> or <see cref="ConformanceOutcome.Inconclusive" /> outcome is
	/// reported as ignored, with the recorded reason, rather than as a pass or a failure.
	/// </summary>
	[TestCaseSource(nameof(Checks))]
	public void CheckOutcomeIsAcceptable(IConformanceCheck check)
	{
		var outcome = Report.Results.Single(result => string.Equals(result.Id, check.Id, StringComparison.Ordinal));
		var result = outcome.Result;

		if (result.Outcome is ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive)
		{
			Assert.Ignore(result.SkipReason ?? "(no reason given)");
		}

		if (result.Outcome == ConformanceOutcome.Failed && check.Requirement == ConformanceRequirement.Required)
		{
			Assert.Fail(
				$"{check.Id} {check.Title}{Environment.NewLine}Expected: {result.Expected}{Environment.NewLine}Actual: {result.Actual}");
		}

		if (result.Outcome == ConformanceOutcome.Failed)
		{
			Assert.Warn(
				$"{check.Id} {check.Title} (Recommended){Environment.NewLine}Expected: {result.Expected}{Environment.NewLine}Actual: {result.Actual}");
		}
	}

	/// <summary>The single, coarse assertion this fixture exists to make: the subject is conformant overall.</summary>
	[Test]
	public void SubjectIsConformant()
	{
		var requiredFailures = Report.Results
			.Where(result => result.Requirement == ConformanceRequirement.Required &&
				result.Result.Outcome == ConformanceOutcome.Failed)
			.Select(result =>
				$"{result.Id} {result.Title}: expected {result.Result.Expected}; actual {result.Result.Actual}")
			.ToList();

		Assert.That(Report.Conformant, Is.True, () => string.Join(Environment.NewLine, requiredFailures));
	}
}
