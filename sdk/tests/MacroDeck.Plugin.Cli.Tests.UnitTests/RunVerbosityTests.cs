using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>run --verbosity quiet</c>, per docs/src/content/docs/guides/packaging.md's verbosity row: <c>quiet</c>
/// suppresses forwarded stub-host log narration, but never the plugin's own console output.
/// </summary>
[TestFixture]
public class RunVerbosityTests
{
	[Test]
	public void A_forwarded_host_log_line_is_suppressed_at_quiet_but_not_at_normal()
	{
		var quietOutput = new StringWriter();
		var quietConsole = new CliConsole(Verbosity.Quiet, noColor: true, quietOutput, new StringWriter());

		var normalOutput = new StringWriter();
		var normalConsole = new CliConsole(Verbosity.Normal, noColor: true, normalOutput, new StringWriter());

		RunSession.WriteForwardedLog(quietConsole, "Information", "Some.Source", "a forwarded host log line");
		RunSession.WriteForwardedLog(normalConsole, "Information", "Some.Source", "a forwarded host log line");

		Assert.Multiple(() =>
		{
			Assert.That(quietOutput.ToString(), Is.Empty);
			Assert.That(normalOutput.ToString(), Does.Contain("a forwarded host log line"));
		});
	}

	[Test]
	public void The_plugins_own_output_is_never_suppressed_and_keeps_the_stream_it_came_from()
	{
		var quietOutput = new StringWriter();
		var quietError = new StringWriter();
		var quiet = new CliConsole(Verbosity.Quiet, noColor: true, quietOutput, quietError);

		RunSession.WritePluginOutput(quiet, "a line the plugin printed", fromStandardError: false);
		RunSession.WritePluginOutput(quiet, "a line the plugin wrote to stderr", fromStandardError: true);

		// cli.md's --verbosity row promises quiet "never suppresses the command's actual result ... a
		// plugin's own console output". Paired with the forwarded-log test above, this is what forces the
		// two streams to be classified rather than both routed through one gate. The stream each line
		// arrives on is preserved as well (#755), so a caller reading only the CLI's stderr still sees what
		// the plugin wrote to its own.
		Assert.Multiple(() =>
		{
			Assert.That(quietOutput.ToString(), Does.Contain("a line the plugin printed"));
			Assert.That(quietOutput.ToString(), Does.Not.Contain("a line the plugin wrote to stderr"));
			Assert.That(quietError.ToString(), Does.Contain("a line the plugin wrote to stderr"));
			Assert.That(quietError.ToString(), Does.Not.Contain("a line the plugin printed"));
		});
	}
}
