namespace MacroDeckHost.Tests.UnitTests.Host;

public class StartupHeartbeatTests
{
	[Test]
	public void The_heartbeat_line_matches_the_bootstrapper()
	{
		Assert.That(StartupHeartbeat.Line, Is.EqualTo("macrodeck-host-heartbeat"));
	}

	[Test]
	public void A_running_heartbeat_keeps_reporting_that_the_host_is_alive()
	{
		var output = new StringWriter();
		var synchronized = TextWriter.Synchronized(output);

		using (StartupHeartbeat.Start(synchronized, TimeSpan.FromMilliseconds(20)))
		{
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
			while (Lines(synchronized, output).Length < 3 && DateTime.UtcNow < deadline)
			{
				Thread.Sleep(10);
			}
		}

		var lines = Lines(synchronized, output);
		Assert.That(lines, Has.Length.AtLeast(3).And.All.EqualTo(StartupHeartbeat.Line));
	}

	[Test]
	public void Nothing_is_reported_once_the_heartbeat_is_disposed()
	{
		var output = new StringWriter();
		var synchronized = TextWriter.Synchronized(output);
		var heartbeat = StartupHeartbeat.Start(synchronized, TimeSpan.FromMilliseconds(10));
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (Lines(synchronized, output).Length == 0 && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(5);
		}

		heartbeat.Dispose();
		var afterDispose = Lines(synchronized, output).Length;
		Thread.Sleep(200);

		Assert.That(Lines(synchronized, output), Has.Length.EqualTo(afterDispose).And.Length.GreaterThan(0));
	}

	[Test]
	public void A_startup_without_a_long_restore_reports_nothing()
	{
		var output = new StringWriter();

		using (StartupHeartbeat.Start(output, StartupHeartbeat.Interval))
		{
		}

		Thread.Sleep(50);

		Assert.That(output.ToString(), Is.Empty);
	}

	private static string[] Lines(TextWriter synchronized, StringWriter output)
	{
		lock (synchronized)
		{
			return output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		}
	}
}
