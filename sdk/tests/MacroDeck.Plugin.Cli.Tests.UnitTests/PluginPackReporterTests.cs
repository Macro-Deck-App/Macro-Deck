using System.Text.RegularExpressions;
using MacroDeck.Plugin.Cli.Packing;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="PluginPackReporter.ReportAsync" />'s failure-reporting half, total over
/// <see cref="PluginPackFailureReason" /> the same way <c>ExitCodeMappingTests</c> is total over its own
/// enums - every reason lands on the documented <c>error &lt;code&gt;: &lt;message&gt;</c> shape and a
/// documented exit code, without this test restating which reason maps to which code (that judgement call
/// belongs to <see cref="PluginPackFailureExitCode" /> alone). A single looping <c>[Test]</c>, not
/// <c>[TestCaseSource]</c>: <see cref="PluginPackFailureReason" /> is internal, and NUnit test methods
/// (unlike their parameters) must be public.
/// </summary>
[TestFixture]
public class PluginPackReporterTests
{
	private static readonly Regex _errorShape = new(@"^error [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.None);

	private static readonly int[] _documentedExitCodes =
	[
		ExitCode.Success, ExitCode.SubjectInvalid, ExitCode.UsageError, ExitCode.InputUnreadable,
		ExitCode.Cancelled, ExitCode.InternalError
	];

	[Test]
	public async Task Every_pack_failure_is_reported_in_the_documented_error_shape()
	{
		foreach (var reason in Enum.GetValues<PluginPackFailureReason>())
		{
			// PluginPackFailureReason.ManifestInvalid is excluded deliberately: PluginPackReporter
			// special-cases it to render the full ManifestValidationResult through ValidationResultWriter on
			// stdout and return that validation's own (data-dependent) exit code, rather than going through
			// the fixed PluginPackFailureExitCode mapping every other reason uses - see
			// PluginPackReporter.ReportFailure.
			if (reason == PluginPackFailureReason.ManifestInvalid)
			{
				continue;
			}

			var output = new StringWriter();
			var error = new StringWriter();
			var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);

			var result = PluginPackResult.Fail(reason, "Something about this pack went wrong.");

			var exitCode = await PluginPackReporter.ReportAsync(console, result, showDigest: false);

			Assert.Multiple(() =>
			{
				Assert.That(error.ToString().TrimEnd('\r', '\n'),
					Does.Match(_errorShape),
					$"{reason} did not report the documented error shape.");
				Assert.That(_documentedExitCodes,
					Does.Contain(exitCode),
					$"{reason} mapped to an undocumented exit code.");
			});
		}
	}
}
