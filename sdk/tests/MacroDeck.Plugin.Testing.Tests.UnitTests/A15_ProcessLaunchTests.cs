using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A15 - a real executable is really launched, and one that dies is visible rather than a hang.
/// </summary>
[TestFixture]
public class A15_ProcessLaunchTests
{
	[Test]
	public async Task The_well_behaved_plugins_built_executable_is_really_launched_and_connects()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable());
		await using var plugin = await host.LaunchAsync(spec);

		Assert.That(plugin.HasExited, Is.False);

		await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		// WaitForSessionAsync signals from the host's side of the handshake - the moment it sent
		// session.welcome - not from the plugin having processed it and updated its own /_macrodeck/ready.
		// That takes one more network round trip, so this polls rather than asserting on a single probe.
		var report = await HealthPolling.WaitUntilAsync(plugin, health => health.Healthy);
		Assert.That(report.Healthy, Is.True);

		// Not asserted here: that StandardOutput/StandardError capture anything for this specific
		// plugin. The fixture calls UseMacroDeckLogging(), which installs Serilog with
		// writeToProviders left at its default false - every log line, including the framework's own
		// "Now listening on..."/"Application started", is routed to the host over log.publish instead
		// of the console provider, so a healthy fixture process is expected to produce no output at
		// all. That the capture mechanism itself works is what
		// A_process_that_dies_before_serving_is_visible_rather_than_a_hang proves, through the
		// misbehaving fixture's stderr line.
	}

	[Test]
	public async Task A_process_that_dies_before_serving_is_visible_rather_than_a_hang()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		// MACRODECK_MISBEHAVE=exit-immediately: the fixture writes a distinctive line to stderr and
		// exits 3 before any plugin machinery starts. Set through the spec, scoped to this one launched
		// process, rather than Environment.SetEnvironmentVariable on the current (test) process, which
		// would leak into every other child process the same run launches.
		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable());
		spec.Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["MACRODECK_MISBEHAVE"] = "exit-immediately"
		};

		var exception = Assert.ThrowsAsync<PluginProcessExitedException>(async () => await host.LaunchAsync(spec));

		Assert.That(exception, Is.Not.Null);
		Assert.That(exception!.Message, Does.Contain("3"));
		Assert.That(exception.Message, Does.Contain("exit-immediately"));

		// The scenario's own ask: HasExited/ExitCode/StandardError asserted on the plugin object
		// itself - not merely parsed back out of the exception's message, which a diagnostic could
		// satisfy without the underlying state actually being right.
		await using var plugin = exception.Plugin;

		Assert.Multiple(() =>
		{
			Assert.That(plugin.HasExited, Is.True);
			Assert.That(plugin.ExitCode, Is.EqualTo(3));
			Assert.That(plugin.StandardError, Does.Contain("exit-immediately"));
		});
	}
}
