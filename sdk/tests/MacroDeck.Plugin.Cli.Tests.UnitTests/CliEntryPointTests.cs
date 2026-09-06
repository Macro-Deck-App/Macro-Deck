using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="CliEntryPoint.RunAsync" /> driven end-to-end with <see cref="StringWriter" />s and no real
/// console or process - the top-level parsing behaviour issue #556's F1-F4 and C4-C6 cases exercise: the
/// bare invocation, a mistyped command, and the two ways <c>inspect</c>'s selector requirement can be
/// violated.
/// </summary>
[TestFixture]
public class CliEntryPointTests
{
	private static readonly Regex _errorShape = new(@"^error [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.None);

	[Test]
	public async Task A_bare_invocation_lists_every_command_and_points_at_help()
	{
		var (output, error, exitCode) = await Run([]);

		var combined = output.ToString() + error.ToString();

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(combined, Does.Contain("validate"));
			Assert.That(combined, Does.Contain("inspect"));
			Assert.That(combined, Does.Contain("pack"));
			Assert.That(combined, Does.Contain("run"));
			Assert.That(combined, Does.Contain("test"));
			Assert.That(combined, Does.Contain("--help"));
		});
	}

	[Test]
	public async Task A_mistyped_command_produces_one_suggestion_and_no_cascade()
	{
		var (output, error, exitCode) = await Run(["pakc", "--source", "."]);

		var combined = output.ToString() + error.ToString();
		var lineCount = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(combined, Does.Contain("pack"));

			// The issue's own complaint: System.CommandLine's default handling would report the typo itself
			// plus every option that followed it as its own unmatched token - four lines of fallout instead
			// of one suggestion.
			Assert.That(combined, Does.Not.Contain("Unrecognized command or argument"));
			Assert.That(combined, Does.Not.Contain("--source"));
			Assert.That(lineCount, Is.LessThanOrEqualTo(2));
		});
	}

	[Test]
	public async Task A_mistyped_command_with_no_near_match_invents_no_suggestion()
	{
		var (output, error, exitCode) = await Run(["zzzzzzzz"]);

		var combined = output.ToString() + error.ToString();

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(combined, Does.Not.Contain("Did you mean"));
		});
	}

	[Test]
	public async Task The_help_names_the_installed_command_and_leaks_no_internal_type_names()
	{
		var (rootOutput, rootError, _) = await Run(["--help"]);
		var (inspectOutput, inspectError, _) = await Run(["inspect", "--help"]);

		var rootCombined = rootOutput.ToString() + rootError.ToString();
		var inspectCombined = inspectOutput.ToString() + inspectError.ToString();

		Assert.Multiple(() =>
		{
			Assert.That(rootCombined, Does.Contain("macrodeck-plugin"));
			Assert.That(rootCombined, Does.Not.Contain("MacroDeck.Plugin.Cli"));
			Assert.That(rootCombined, Does.Not.Contain("PluginArtifactDigest.Compute"));

			Assert.That(inspectCombined, Does.Contain("macrodeck-plugin"));
			Assert.That(inspectCombined, Does.Not.Contain("MacroDeck.Plugin.Cli"));
			Assert.That(inspectCombined, Does.Not.Contain("PluginArtifactDigest.Compute"));
		});
	}

	[Test]
	public async Task Inspect_with_neither_selector_and_with_both_produce_different_messages()
	{
		// --no-color so the assertions compare the message, not the ANSI escapes wrapped around it.
		var (neitherOutput, neitherError, neitherExitCode) = await Run(["inspect", "--no-color"]);
		var (bothOutput, bothError, bothExitCode) =
			await Run(["inspect", "--artifact", "a", "--directory", "b", "--no-color"]);

		var neitherLine = (neitherOutput.ToString() + neitherError.ToString()).Trim();
		var bothLine = (bothOutput.ToString() + bothError.ToString()).Trim();

		Assert.Multiple(() =>
		{
			Assert.That(neitherExitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(bothExitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(neitherLine, Does.Match(_errorShape));
			Assert.That(bothLine, Does.Match(_errorShape));

			// The whole point: no reformatting of one shared string ("Specify exactly one of...") for both
			// mistakes can pass this - the issue's own complaint was that both cases read identically.
			Assert.That(neitherLine, Is.Not.EqualTo(bothLine));
		});
	}

	[Test]
	public async Task Inspect_still_requires_a_selector_rather_than_defaulting_to_the_current_directory()
	{
		var directory = ManifestFixtures.WriteValidManifestDirectory();
		var originalDirectory = Environment.CurrentDirectory;

		try
		{
			Environment.CurrentDirectory = directory;

			var (_, _, exitCode) = await Run(["inspect"]);

			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
		}
		finally
		{
			Environment.CurrentDirectory = originalDirectory;
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task A_rejected_global_option_value_is_a_usage_error_not_a_crash()
	{
		// The console that reports a parse error cannot itself depend on that parse having succeeded -
		// reading --verbosity's value throws once its own parser rejected it. The documented contract is
		// exit 2 for bad arguments, and the whole point of this issue is that no failure ends in a raw
		// stack trace.
		var (output, error, exitCode) = await Run(["--no-color", "--verbosity", "bogus", "validate"]);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error.ToString(), Does.Match(_errorShape));
			Assert.That(output.ToString() + error.ToString(), Does.Not.Contain("Unhandled exception"));
			Assert.That(output.ToString() + error.ToString(), Does.Not.Contain("   at "));
		});
	}

	private static async Task<(StringWriter Output, StringWriter Error, int ExitCode)> Run(string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error);
		return (output, error, exitCode);
	}
}
