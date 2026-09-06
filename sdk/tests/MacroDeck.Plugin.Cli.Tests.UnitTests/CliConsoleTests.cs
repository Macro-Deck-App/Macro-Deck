using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="CliConsole" />'s own diagnostic shape - the <c>error &lt;code&gt;: &lt;message&gt;</c> /
/// <c>warning &lt;code&gt;: &lt;message&gt;</c> convention issue #556 asks every command to share, and the
/// verbosity/color rules that shape has to hold under.
/// </summary>
[TestFixture]
public class CliConsoleTests
{
	private static readonly Regex _errorShape = new(@"^error [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.None);

	private static readonly Regex _warningShape = new(@"^warning [a-z0-9]+(-[a-z0-9]+)*: \S", RegexOptions.None);

	[Test]
	public void WriteError_writes_the_documented_error_shape_to_stderr_only()
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);

		console.WriteError("something-wrong", "Something went wrong.");

		Assert.Multiple(() =>
		{
			Assert.That(output.ToString(), Is.Empty);
			Assert.That(error.ToString().TrimEnd('\r', '\n'), Does.Match(_errorShape));
		});
	}

	[Test]
	public void WriteWarning_writes_the_documented_warning_shape()
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);

		console.WriteWarning("something-off", "Something is worth a second look.");

		Assert.Multiple(() =>
		{
			Assert.That(output.ToString(), Is.Empty);
			Assert.That(error.ToString().TrimEnd('\r', '\n'), Does.Match(_warningShape));
		});
	}

	[Test]
	public void A_diagnostic_is_written_even_at_quiet_verbosity_while_narration_is_not()
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var console = new CliConsole(Verbosity.Quiet, noColor: true, output, error);

		console.WriteError("still-reported", "Quiet never silences the result.");
		console.WriteWarning("still-reported-too", "Neither does this.");
		console.Info("Narration that quiet is meant to suppress.");

		Assert.Multiple(() =>
		{
			Assert.That(error.ToString(), Does.Contain("error still-reported:"));
			Assert.That(error.ToString(), Does.Contain("warning still-reported-too:"));
			Assert.That(output.ToString(), Is.Empty);
		});
	}

	[Test]
	public void No_color_leaves_no_ansi_escape_in_a_diagnostic()
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var console = new CliConsole(Verbosity.Normal, noColor: true, output, error);

		console.WriteError("plain-text", "No escape codes here.");
		console.WriteWarning("plain-text-too", "None here either.");

		Assert.That(error.ToString(), Does.Not.Contain(''));
	}
}
