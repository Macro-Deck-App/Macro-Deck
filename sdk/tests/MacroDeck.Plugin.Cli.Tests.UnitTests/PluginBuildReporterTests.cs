using System.Text.RegularExpressions;
using MacroDeck.Plugin.Cli.Building;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="PluginBuildReporter.ReportAsync" />'s failure-reporting half, total over
/// <see cref="PluginBuildFailureReason" /> the way <see cref="PluginPackReporterTests" /> is over its own
/// enum: every reason lands on the documented <c>error &lt;code&gt;: &lt;message&gt;</c> shape and a
/// documented exit code, without restating which reason maps to which code - that judgement belongs to
/// <see cref="PluginBuildFailureExitCode" /> alone. A single looping <c>[Test]</c>, not
/// <c>[TestCaseSource]</c>, because the enum is internal.
/// </summary>
[TestFixture]
public class PluginBuildReporterTests
{
	private static readonly Regex _errorShape = new(@"^error [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.None);

	private static readonly int[] _documentedExitCodes =
	[
		ExitCode.Success, ExitCode.SubjectInvalid, ExitCode.UsageError, ExitCode.InputUnreadable,
		ExitCode.Cancelled, ExitCode.InternalError
	];

	[Test]
	public async Task Every_build_failure_is_reported_in_the_documented_error_shape()
	{
		foreach (var reason in Enum.GetValues<PluginBuildFailureReason>())
		{
			var error = new StringWriter();
			var console = new CliConsole(Verbosity.Normal, noColor: true, new StringWriter(), error);

			var exitCode = await PluginBuildReporter.ReportAsync(console,
				PluginBuildResult.Fail(reason, $"Something went wrong building for {reason}."));

			Assert.Multiple(() =>
			{
				Assert.That(_errorShape.IsMatch(error.ToString()),
					Is.True,
					$"{reason} reported '{error.ToString().TrimEnd()}'.");
				Assert.That(exitCode, Is.AnyOf(_documentedExitCodes), $"{reason} returned {exitCode}.");
				Assert.That(exitCode, Is.Not.EqualTo(ExitCode.Success), $"{reason} reported success.");
			});
		}
	}

	[Test]
	public async Task A_failing_build_tools_output_is_surfaced_below_the_error_line()
	{
		var error = new StringWriter();
		var console = new CliConsole(Verbosity.Normal, noColor: true, new StringWriter(), error);

		// A developer cannot act on "the build failed"; the tool's own text is the actionable part.
		await PluginBuildReporter.ReportAsync(console,
			PluginBuildResult.Fail(PluginBuildFailureReason.BuildFailed,
				"Building 'win-x64' failed.",
				"error CS1002: ; expected"));

		Assert.That(error.ToString(), Does.Contain("error CS1002: ; expected"));
	}
}
