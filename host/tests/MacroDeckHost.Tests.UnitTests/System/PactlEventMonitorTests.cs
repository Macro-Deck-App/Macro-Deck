using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

[Platform("Linux,MacOsX")]
public class PactlEventMonitorTests
{
	private static readonly IReadOnlyDictionary<string, string?> _noEnvironment = new Dictionary<string, string?>();

	private static PactlEventMonitor Monitor(string script, Action onEvent, TimeSpan? minBackoff = null)
	{
		return new PactlEventMonitor(onEvent,
			_noEnvironment,
			"sh",
			["-c", script],
			TimeSpan.FromMilliseconds(20),
			minBackoff ?? TimeSpan.FromMilliseconds(50),
			TimeSpan.FromMilliseconds(200));
	}

	[Test]
	public async Task Starting_resyncs_once_and_client_noise_raises_nothing_more()
	{
		var raised = 0;
		using var monitor = Monitor("echo \"Event 'new' on client #3\"; echo \"Event 'remove' on client #3\"; sleep 30",
			() => Interlocked.Increment(ref raised));

		monitor.Start();

		await Task.Delay(500);
		Assert.That(Volatile.Read(ref raised), Is.EqualTo(1));
	}

	[Test]
	public async Task A_sink_event_after_the_start_raises_again()
	{
		var raised = 0;
		using var monitor = Monitor("sleep 0.4; echo \"Event 'change' on sink #1\"; sleep 30",
			() => Interlocked.Increment(ref raised));

		monitor.Start();

		await Eventually(() => Volatile.Read(ref raised) >= 2);
	}

	[Test]
	public async Task A_burst_of_events_collapses_into_a_few_raises()
	{
		var raised = 0;
		using var monitor = Monitor(
			"i=0; while [ $i -lt 200 ]; do echo \"Event 'change' on sink #1\"; i=$((i+1)); done; sleep 30",
			() => Interlocked.Increment(ref raised));

		monitor.Start();

		await Task.Delay(500);
		Assert.That(Volatile.Read(ref raised), Is.InRange(1, 10));
	}

	[Test]
	public async Task A_subscriber_that_exits_is_started_again()
	{
		var raised = 0;
		using var monitor = Monitor("echo \"Event 'change' on server #1\"", () => Interlocked.Increment(ref raised));

		monitor.Start();

		await Eventually(() => Volatile.Read(ref raised) >= 3);
	}

	[Test]
	public async Task A_subscriber_that_fails_at_once_is_retried_with_a_delay_not_in_a_tight_loop()
	{
		var raised = 0;
		using var monitor = Monitor("exit 1", () => Interlocked.Increment(ref raised), TimeSpan.FromMilliseconds(100));

		monitor.Start();

		await Task.Delay(400);
		Assert.That(Volatile.Read(ref raised), Is.InRange(1, 4));
	}

	[Test]
	public async Task Disposing_stops_the_subscriber_and_further_events()
	{
		var raised = 0;
		var monitor = Monitor("echo \"Event 'change' on sink #1\"; sleep 30", () => Interlocked.Increment(ref raised));
		monitor.Start();
		await Eventually(() => Volatile.Read(ref raised) >= 1);

		monitor.Dispose();
		var after = Volatile.Read(ref raised);
		await Task.Delay(300);

		Assert.That(Volatile.Read(ref raised), Is.EqualTo(after));
	}

	[Test]
	public async Task It_reports_listening_only_while_the_subscriber_runs()
	{
		using var monitor = Monitor("sleep 0.5", Ignore, TimeSpan.FromSeconds(30));
		Assert.That(monitor.ListeningSince, Is.EqualTo(PactlEventMonitor.NotListening));

		monitor.Start();
		await Eventually(() => monitor.ListeningSince != PactlEventMonitor.NotListening);

		await Eventually(() => monitor.ListeningSince == PactlEventMonitor.NotListening);
	}

	[Test]
	public async Task Disposing_ends_listening()
	{
		var monitor = Monitor("sleep 30", Ignore);
		monitor.Start();
		await Eventually(() => monitor.ListeningSince != PactlEventMonitor.NotListening);

		monitor.Dispose();

		Assert.That(monitor.ListeningSince, Is.EqualTo(PactlEventMonitor.NotListening));
	}

	private static void Ignore()
	{
	}

	private static async Task Eventually(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "The condition never became true");
			await Task.Delay(20);
		}
	}
}
