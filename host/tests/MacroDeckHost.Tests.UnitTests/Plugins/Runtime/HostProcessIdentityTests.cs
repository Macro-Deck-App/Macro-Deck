using System.Diagnostics;
using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class HostProcessIdentityTests
{
	private const string Stat = "42 (Macro Deck) (host) S 1 42 42 0 -1 4194304 1 0 0 0 1 1 0 0 20 0 9 0 12345 1 1";

	private static readonly DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void The_start_time_is_built_from_the_current_clock_even_with_spaces_in_the_process_name()
	{
		var startedAt = HostProcessIdentity.FromProcFs(Stat, "1000.50 2000.00\n", _now);

		Assert.That(startedAt, Is.EqualTo(_now - TimeSpan.FromSeconds(1000.5) + TimeSpan.FromSeconds(123.45)));
	}

	[Test]
	public void A_clock_step_moves_the_value_with_the_clock()
	{
		var before = HostProcessIdentity.FromProcFs(Stat, "1000.00", _now);
		var after = HostProcessIdentity.FromProcFs(Stat, "1000.00", _now.AddSeconds(30));

		Assert.That(after - before, Is.EqualTo(TimeSpan.FromSeconds(30)));
	}

	[TestCase("not a stat line", "100.00")]
	[TestCase("1 (x) S 1", "100.00")]
	[TestCase(Stat, "garbage")]
	public void Unreadable_proc_files_give_no_value(string stat, string uptime)
	{
		Assert.That(HostProcessIdentity.FromProcFs(stat, uptime, _now), Is.Null);
	}

	[Test]
	[Platform("Linux")]
	public void On_linux_the_value_matches_what_a_plugin_reads_for_the_host_process()
	{
		using var process = Process.GetCurrentProcess();

		var drift = HostProcessIdentity.StartedAtForLaunch() - process.StartTime.ToUniversalTime();

		Assert.That(drift.Duration(), Is.LessThan(TimeSpan.FromSeconds(1)));
	}
}
