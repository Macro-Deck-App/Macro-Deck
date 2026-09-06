using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>B10: genuinely reusable by a third party.</summary>
[TestFixture]
internal sealed class ReusabilityTests
{
	private static readonly string[] _forbiddenAssemblyNameFragments =
	[
		"nunit.framework",
		"xunit",
		"Microsoft.VisualStudio.TestPlatform",
		"FluentAssertions",
		"Shouldly"
	];

	/// <summary>
	/// "No test-framework types in the core" is invisible to any behavioural test - the only way to prove it
	/// is to inspect what the compiled assembly actually references.
	/// </summary>
	[Test]
	public void CoreAssemblyReferencesNoTestFrameworkAssembly()
	{
		var referencedNames = typeof(ConformanceRunner).Assembly.GetReferencedAssemblies()
			.Select(name => name.Name ?? string.Empty)
			.ToList();

		foreach (var forbidden in _forbiddenAssemblyNameFragments)
		{
			Assert.That(referencedNames,
				Has.None.Matches<string>(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
				$"MacroDeck.Plugin.Testing.dll references an assembly matching '{forbidden}', which would tie the " +
				"framework-agnostic conformance core to one specific test framework.");
		}
	}

	/// <summary>
	/// Running the same Checks against the fixture as all three subject kinds yields reports whose set of
	/// check ids is identical; a check that passes in-process and does not pass as an executable must be
	/// Skipped, never Failed. The in-process third reuses <see cref="Support.SharedInProcessConformanceRun" />
	/// rather than running its own copy of the same suite against the same subject -
	/// <see cref="WellBehavedInProcessSuiteTests" /> and <see cref="ReportHonestyTests" /> already need exactly
	/// this same report too.
	/// </summary>
	[Test]
	public async Task SameCheckIdsAcrossAllThreeSubjectKinds()
	{
		var runner = new ConformanceRunner();
		var expectedIds = runner.Checks.Select(check => check.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

		var inProcessReport = await SharedInProcessConformanceRun.GetReportAsync();

		Assert.That(inProcessReport.Results.Select(result => result.Id).OrderBy(id => id, StringComparer.Ordinal),
			Is.EqualTo(expectedIds));

		var executable
			= ConformanceSubject.Executable(
				PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		ConformanceReport executableReport;

		try
		{
			executableReport = await runner.RunAsync(executable);
		}
		finally
		{
			await executable.DisposeAsync();
		}

		Assert.That(executableReport.Results.Select(result => result.Id).OrderBy(id => id, StringComparer.Ordinal),
			Is.EqualTo(expectedIds));

		var artifactPath = ConformanceArtifactBuilder.BuildFrameworkDependentArtifact(
			PluginLocator.FindWellBehavedPluginBuildDirectory(),
			"MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.dll",
			"app.macro-deck.well-behaved-test-plugin",
			"Sample",
			"1.0.0");

		var artifact = ConformanceSubject.Artifact(artifactPath);
		ConformanceReport artifactReport;

		try
		{
			artifactReport = await runner.RunAsync(artifact);
		}
		finally
		{
			await artifact.DisposeAsync();
		}

		Assert.That(artifactReport.Results.Select(result => result.Id).OrderBy(id => id, StringComparer.Ordinal),
			Is.EqualTo(expectedIds));

		foreach (var inProcessResult in inProcessReport.Results.Where(result =>
			result.Result.Outcome == ConformanceOutcome.Passed))
		{
			var executableResult = executableReport.Results.Single(result =>
				string.Equals(result.Id, inProcessResult.Id, StringComparison.Ordinal));

			Assert.That(executableResult.Result.Outcome,
				Is.Not.EqualTo(ConformanceOutcome.Failed),
				$"{inProcessResult.Id} passed in-process but failed as an executable - it should have reported Skipped instead if it does not apply to that subject kind.");

			var artifactResult = artifactReport.Results.Single(result =>
				string.Equals(result.Id, inProcessResult.Id, StringComparison.Ordinal));

			Assert.That(artifactResult.Result.Outcome,
				Is.Not.EqualTo(ConformanceOutcome.Failed),
				$"{inProcessResult.Id} passed in-process but failed as an artifact - it should have reported Skipped instead if it does not apply to that subject kind.");
		}
	}
}
