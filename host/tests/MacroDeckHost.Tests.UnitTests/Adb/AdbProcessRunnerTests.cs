using System.Diagnostics;
using MacroDeckHost.Infrastructure.Adb;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbProcessRunnerTests
{
	[Test]
	public async Task A_command_that_outlives_its_timeout_is_killed_and_reports_TimedOut()
	{
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger());
		var stopwatch = Stopwatch.StartNew();

		var result = await runner.RunAsync("sleep", ["5"], TimeSpan.FromMilliseconds(300), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Started, Is.True);
			Assert.That(result.TimedOut, Is.True);
			Assert.That(stopwatch.Elapsed,
				Is.LessThan(TimeSpan.FromSeconds(4)),
				"the timeout must actually kill the process rather than waiting for it to exit on its own");
		});
	}

	[Test]
	public async Task RunBinaryAsync_returns_the_exact_bytes_the_process_wrote()
	{
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger());
		// Octal escapes for a null byte, a byte outside valid UTF-8 on its own, and a plain ASCII byte:
		// a StreamReader-based read would decode these as text and corrupt them on re-encoding.
		var expected = new byte[] { 0x00, 0xFF, 0x41 };

		var result = await runner.RunBinaryAsync("printf",
			[@"\000\377\101"],
			TimeSpan.FromSeconds(5),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Started, Is.True);
			Assert.That(result.TimedOut, Is.False);
			Assert.That(result.StandardOutput, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task RunAsync_reports_Started_false_rather_than_throwing_when_the_executable_does_not_exist()
	{
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger());
		var missingExecutable = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));

		var result = await runner.RunAsync(missingExecutable, [], TimeSpan.FromSeconds(5), CancellationToken.None);

		Assert.That(result.Started, Is.False);
	}
}
