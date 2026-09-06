using System.Text.Json;
using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>
/// B9: the worst possible bug in this deliverable is a suite that green-lights a plugin it never
/// exercised. Asserts every honesty property section B lists holds for one real report - reused from
/// <see cref="Support.SharedInProcessConformanceRun" /> rather than run again here, since
/// <see cref="WellBehavedInProcessSuiteTests" /> and <see cref="ReusabilityTests" /> already need the identical
/// run against the identical subject. The subject itself is disposed once, by this assembly's own
/// <see cref="NUnit.Framework.SetUpFixtureAttribute" />, after every fixture that might still request it
/// has had its turn.
/// </summary>
[TestFixture]
internal sealed class ReportHonestyTests
{
	private ConformanceReport? _report;

	private ConformanceReport Report => _report ?? throw new InvalidOperationException("The suite has not run yet.");

	[OneTimeSetUp]
	public async Task RunSuiteAsync() => _report = await SharedInProcessConformanceRun.GetReportAsync();

	[Test]
	public void ResultsHasExactlyOneEntryPerCheckNoneDropped()
	{
		var expectedIds = new ConformanceRunner().Checks.Select(check => check.Id).ToList();

		Assert.That(Report.Results.Select(result => result.Id), Is.EquivalentTo(expectedIds));
		Assert.That(Report.Results.Select(result => result.Id), Is.Unique);
	}

	[Test]
	public void CountsEqualTheTallies()
	{
		Assert.That(Report.Passed + Report.Failed + Report.Skipped, Is.EqualTo(Report.Results.Count));
		Assert.That(Report.Passed,
			Is.EqualTo(Report.Results.Count(result => result.Result.Outcome == ConformanceOutcome.Passed)));
		Assert.That(Report.Failed,
			Is.EqualTo(Report.Results.Count(result => result.Result.Outcome == ConformanceOutcome.Failed)));

		Assert.That(Report.Skipped,
			Is.EqualTo(Report.Results.Count(result =>
				result.Result.Outcome is ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive)));
	}

	[Test]
	public void AnUnmetPreconditionSkipsWithANonEmptyReasonNeverPasses()
	{
		// MDC0205 only ever applies to a managed, externally launched subject (its own body checks
		// context.Plugin is ExternalPlugin) - the ambient in-process subject always self-registers, so it
		// is guaranteed to leave this one unmet regardless of what a subject's own manifest looks like.
		// MDC0104 no longer fits this example: since #522 every plugin has a manifest, including an
		// in-process one (see ConformanceSubject.InProcess's own manifest parameter), so it now runs -
		// and passes - here too instead of skipping.
		var managedOnlyCheck
			= Report.Results.Single(result => string.Equals(result.Id, "MDC0205", StringComparison.Ordinal));

		Assert.That(managedOnlyCheck.Result.Outcome, Is.EqualTo(ConformanceOutcome.Skipped));
		Assert.That(managedOnlyCheck.Result.SkipReason, Is.Not.Null.And.Not.Empty);

		foreach (var result in Report.Results.Where(result =>
			result.Result.Outcome is ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive))
		{
			Assert.That(result.Result.SkipReason,
				Is.Not.Null.And.Not.Empty,
				$"{result.Id} was skipped/inconclusive with no reason.");
		}
	}

	[Test]
	public void EveryFailedResultCarriesNonEmptyExpectedAndActual()
	{
		foreach (var result in Report.Results.Where(result => result.Result.Outcome == ConformanceOutcome.Failed))
		{
			Assert.That(result.Result.Expected, Is.Not.Null.And.Not.Empty, $"{result.Id} failed with no Expected.");
			Assert.That(result.Result.Actual, Is.Not.Null.And.Not.Empty, $"{result.Id} failed with no Actual.");
		}
	}

	[Test]
	public void ARequiredFailureAndOnlyARequiredFailureBlocksConformant()
	{
		var anyRequiredFailure = Report.Results
			.Any(result => result.Requirement == ConformanceRequirement.Required &&
				result.Result.Outcome == ConformanceOutcome.Failed);

		Assert.That(Report.Conformant, Is.EqualTo(!anyRequiredFailure));
	}

	[Test]
	public void PluginIdAndVersionComeFromTheSubject()
	{
		Assert.That(Report.PluginId, Is.EqualTo("app.macro-deck.well-behaved-test-plugin"));
		Assert.That(Report.PluginVersion, Is.EqualTo("1.0.0"));
	}

	[Test]
	public void AllThreeWritersRenderAFailureAsAFailure()
	{
		// A synthetic report with one deliberately failed entry, so this does not depend on the fixture
		// plugin actually failing something to prove the writers render a failure correctly.
		var failing = SyntheticReportWithOneFailure();

		Assert.That(ConformanceReportWriter.ToText(failing), Does.Contain("FAIL"));
		Assert.That(ConformanceReportWriter.ToMarkdown(failing), Does.Contain("FAIL"));

		using var document = JsonDocument.Parse(ConformanceReportWriter.ToJson(failing));
		var outcome = document.RootElement.GetProperty("results")[0].GetProperty("result").GetProperty("outcome")
			.GetString();
		Assert.That(outcome, Is.EqualTo("failed"));
	}

	[Test]
	public void ToJsonRoundTripsToTheSamePerCheckOutcomes()
	{
		using var document = JsonDocument.Parse(ConformanceReportWriter.ToJson(Report));
		var results = document.RootElement.GetProperty("results");

		Assert.That(results.GetArrayLength(), Is.EqualTo(Report.Results.Count));

		for (var i = 0; i < Report.Results.Count; i++)
		{
			var expected = Report.Results[i];
			var actualEntry = results[i];

			Assert.That(actualEntry.GetProperty("id").GetString(), Is.EqualTo(expected.Id));

			Assert.That(actualEntry.GetProperty("result").GetProperty("outcome").GetString(),
				Is.EqualTo(expected.Result.Outcome.ToString().ToLowerInvariant()));
		}
	}

	private static ConformanceReport SyntheticReportWithOneFailure()
	{
		var check = new ConformanceRunner().Checks[0];
		var failedResult = ConformanceCheckResult.Fail("expected value", "actual value");

		var outcome = new ConformanceCheckOutcome
		{
			Id = check.Id,
			Title = check.Title,
			Category = check.Category,
			Requirement = check.Requirement,
			Result = failedResult
		};

		return new ConformanceReport
		{
			SuiteVersion = ConformanceRunner.SuiteVersion,
			PluginId = "test.plugin",
			PluginVersion = "1.0.0",
			StartedAt = DateTimeOffset.UtcNow,
			Duration = TimeSpan.FromSeconds(1),
			Results = [outcome],
			Passed = 0,
			Failed = 1,
			Skipped = 0,
			Conformant = false
		};
	}
}
