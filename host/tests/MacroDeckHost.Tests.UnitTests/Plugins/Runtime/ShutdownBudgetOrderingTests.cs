using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeckHost.Infrastructure.BackgroundServices;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class ShutdownBudgetOrderingTests
{
	[Test]
	public void The_supervisors_plugin_budget_finishes_inside_the_hosts_shutdown_timeout()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PluginShutdownBudgets.SupervisorPluginBudget, Is.EqualTo(TimeSpan.FromSeconds(25)));
			Assert.That(PluginShutdownBudgets.HostShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
			Assert.That(PluginShutdownBudgets.SupervisorPluginBudget,
				Is.LessThan(PluginShutdownBudgets.HostShutdownTimeout));
		});
	}

	[Test]
	public void The_hosts_shutdown_timeout_finishes_inside_the_bootstrappers_graceful_budget()
	{
		var quitTimeout = ReadBootstrapperTimeout("QUIT_STOP_TIMEOUT");

		Assert.Multiple(() =>
		{
			Assert.That(PluginShutdownBudgets.SupervisorPluginBudget,
				Is.LessThan(PluginShutdownBudgets.HostShutdownTimeout));
			Assert.That(PluginShutdownBudgets.HostShutdownTimeout, Is.LessThan(quitTimeout));
		});
	}

	[Test]
	public void The_bootstrappers_forced_stop_budget_stays_inside_its_graceful_budget()
	{
		var quitTimeout = ReadBootstrapperTimeout("QUIT_STOP_TIMEOUT");
		var forcedTimeout = ReadBootstrapperTimeout("FORCED_STOP_TIMEOUT");

		Assert.That(forcedTimeout, Is.LessThan(quitTimeout));
	}

	private static TimeSpan ReadBootstrapperTimeout(string constantName)
	{
		var source = File.ReadAllText(BootstrapperHostSourcePath());
		var match = Regex.Match(source,
			$@"const\s+{Regex.Escape(constantName)}\s*:\s*Duration\s*=\s*Duration::from_secs\((\d+)\)",
			RegexOptions.None,
			TimeSpan.FromSeconds(5));

		Assert.That(match.Success,
			Is.True,
			$"'{constantName}' was not found in the bootstrapper's host.rs; the shutdown budgets can no longer " +
			"be checked against each other.");

		return TimeSpan.FromSeconds(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
	}

	private static string BootstrapperHostSourcePath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return Path.Combine(directory.FullName, "ui", "bootstrapper", "src", "host.rs");
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate the repository root above the test output directory.");
	}
}
