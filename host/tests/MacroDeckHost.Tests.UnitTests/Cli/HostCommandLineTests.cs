using MacroDeckHost.Cli;

namespace MacroDeckHost.Tests.UnitTests.Cli;

[TestFixture]
internal sealed class HostCommandLineTests
{
	[Test]
	public void A_plain_start_runs_the_host_and_keeps_every_argument()
	{
		var commandLine = HostCommandLine.Parse(["--urls", "http://localhost:5000"]);

		Assert.Multiple(() =>
		{
			Assert.That(commandLine.Command, Is.EqualTo(HostCommand.Run));
			Assert.That(commandLine.DisablePlugins, Is.False);
			Assert.That(commandLine.RemainingArguments, Is.EqualTo(new[] { "--urls", "http://localhost:5000" }));
		});
	}

	[Test]
	public void Disable_plugins_runs_the_host_and_is_not_handed_on_as_configuration()
	{
		var commandLine = HostCommandLine.Parse(["--disable-plugins"]);

		Assert.Multiple(() =>
		{
			Assert.That(commandLine.Command, Is.EqualTo(HostCommand.Run));
			Assert.That(commandLine.DisablePlugins, Is.True);
			Assert.That(commandLine.RemainingArguments, Is.Empty);
		});
	}

	[TestCase(new[] { "--help" }, HostCommand.Help)]
	[TestCase(new[] { "-h" }, HostCommand.Help)]
	[TestCase(new[] { "--version" }, HostCommand.Version)]
	[TestCase(new[] { "--list-plugins" }, HostCommand.ListPlugins)]
	[TestCase(new[] { "--list-plugins", "--version", "--help" }, HostCommand.Help)]
	[TestCase(new[] { "--list-plugins", "--version" }, HostCommand.Version)]
	[TestCase(new[] { "--disable-plugins", "--list-plugins" }, HostCommand.ListPlugins)]
	public void An_information_option_wins_over_starting_the_host(string[] arguments, HostCommand expected)
	{
		Assert.That(HostCommandLine.Parse(arguments).Command, Is.EqualTo(expected));
	}
}
