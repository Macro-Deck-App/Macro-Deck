using System.Diagnostics;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A16 - <c>StopGracefullyAsync</c> reports truthfully, and the documented sequence is what the plugin
/// actually sees.
/// </summary>
[TestFixture]
public class A16_GracefulShutdownTests
{
	[Test]
	public async Task The_well_behaved_plugin_exits_gracefully_within_the_grace_period()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable());
		await using var plugin = await host.LaunchAsync(spec);
		await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var grace = TimeSpan.FromSeconds(10);
		var report = await plugin.StopGracefullyAsync(grace);

		Assert.Multiple(() =>
		{
			Assert.That(report.ExitedWithinGrace, Is.True);
			Assert.That(report.Killed, Is.False);
			Assert.That(report.ExitCode, Is.EqualTo(0));
			Assert.That(report.Elapsed, Is.LessThan(grace));
			Assert.That(plugin.HasExited, Is.True);
		});

		// The documented sequence: session.goodbye reaches the plugin strictly before the close carrying
		// SupervisorShutdown. An author whose cleanup runs on session.goodbye would get a false failure
		// from a test host that only closed the socket - see StopGracefullyAsync's own remarks.
		var goodbye = host.Messages.All.Single(message
			=> message.Direction == ProtocolMessageDirection.ToPlugin &&
			message.Envelope.Type == MessageTypes.SessionGoodbye);
		var shutdownClose
			= host.Messages.Closes.Single(close => close.CloseCode == ProtocolCloseCodes.SupervisorShutdown);

		Assert.That(goodbye.Sequence,
			Is.LessThan(shutdownClose.Sequence),
			"session.goodbye must reach the plugin strictly before the close carrying SupervisorShutdown");
	}

	[Test]
	public async Task StopGracefullyAsync_targets_only_its_own_plugin_when_two_are_live()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		// Two *different* plugins, not the same executable twice: a plugin's id comes from its own
		// manifest.json, and ProtocolLimits.MaxSessionsPerPlugin is 1, so two instances of one id can
		// never both be live - the second session replaces the first, and that replacement is a fatal
		// close that stops the loser. Launching the well-behaved and the misbehaving fixture gives two
		// genuinely independent sessions, which is what this test needs to say anything about targeting.
		await using var first
			= await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		await using var second =
			await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable()));
		await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var report = await first.StopGracefullyAsync(TimeSpan.FromSeconds(10));

		// Without its own session reference, StopGracefullyAsync resolves MacroDeckTestHost's most
		// recently connected plugin - "second" here - so it would say goodbye to the wrong process:
		// "first" would hang past its grace period and be killed, and "second" would exit though nobody
		// asked it to.
		Assert.Multiple(() =>
		{
			Assert.That(report.ExitedWithinGrace, Is.True);
			Assert.That(report.Killed, Is.False);
			Assert.That(first.HasExited, Is.True);
			Assert.That(second.HasExited, Is.False, "stopping the first plugin must not affect the second");
		});
	}

	[Test]
	public async Task A_plugin_blocking_in_StopAsync_is_killed_after_the_grace_period()
	{
		await using var host = await MacroDeckTestHost.StartAsync();

		// Set through the spec, scoped to this one launched process - see A15's identical remarks on why
		// not Environment.SetEnvironmentVariable on the current (test) process.
		var spec = PluginLaunchSpec.ForExecutable(PluginLocator.FindMisbehavingPluginExecutable());
		spec.Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
			{ ["MACRODECK_MISBEHAVE"] = "hang-stop" };

		await using var plugin = await host.LaunchAsync(spec);
		await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var grace = TimeSpan.FromSeconds(2);
		var stopwatch = Stopwatch.StartNew();
		var report = await plugin.StopGracefullyAsync(grace);
		stopwatch.Stop();

		Assert.Multiple(() =>
		{
			Assert.That(report.ExitedWithinGrace, Is.False);
			Assert.That(report.Killed, Is.True);
			Assert.That(report.Elapsed, Is.GreaterThanOrEqualTo(grace));
			Assert.That(plugin.HasExited, Is.True);
		});
	}
}
