using System.Diagnostics;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Infrastructure.Adb;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbProcessRunnerTests
{
	[Test]
	public async Task A_command_that_outlives_its_timeout_is_killed_and_reports_TimedOut()
	{
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger(), new UserSessionEnd());
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
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger(), new UserSessionEnd());
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
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger(), new UserSessionEnd());
		var missingExecutable = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));

		var result = await runner.RunAsync(missingExecutable, [], TimeSpan.FromSeconds(5), CancellationToken.None);

		Assert.That(result.Started, Is.False);
	}

	[Test]
	public async Task Nothing_is_started_once_the_user_session_is_ending()
	{
		var sessionEnd = new UserSessionEnd();
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger(), sessionEnd);
		var directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var spawnedBefore = Path.Combine(directory, "before");
		var spawnedAfter = Path.Combine(directory, "after");

		try
		{
			var before = await runner.RunAsync("touch", [spawnedBefore], TimeSpan.FromSeconds(5), CancellationToken.None);
			sessionEnd.Mark();
			var after = await runner.RunAsync("touch", [spawnedAfter], TimeSpan.FromSeconds(5), CancellationToken.None);
			var binary = await runner.RunBinaryAsync("touch", [spawnedAfter], TimeSpan.FromSeconds(5), CancellationToken.None);
			var bounded = await runner.RunBoundedAsync("touch",
				[spawnedAfter],
				TimeSpan.FromSeconds(5),
				1024,
				CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(before.Started && File.Exists(spawnedBefore), Is.True, "precondition: the runner spawns normally");
				Assert.That(after.Started || binary.Started || bounded.Started, Is.False);
				Assert.That(File.Exists(spawnedAfter), Is.False, "no process may run once the session is ending");
			});
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Test]
	public async Task Nothing_is_started_while_the_operating_system_reports_the_session_shutting_down()
	{
		using var runner = new AdbProcessRunner(new LoggerConfiguration().CreateLogger(), new UserSessionEnd(() => true));
		var spawned = Path.Combine(Path.GetTempPath(), $"macro-deck-spawn-{Guid.NewGuid():N}");

		var result = await runner.RunAsync("touch", [spawned], TimeSpan.FromSeconds(5), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Started, Is.False);
			Assert.That(File.Exists(spawned), Is.False);
		});
	}

	[Test]
	public async Task The_registered_runner_refuses_once_the_registered_session_end_is_marked()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILogger>(new LoggerConfiguration().CreateLogger());
		services.AddAdbManager();
		await using var provider = services.BuildServiceProvider();
		var runner = provider.GetRequiredService<AdbProcessRunner>();
		provider.GetRequiredService<UserSessionEnd>().Mark();
		var spawned = Path.Combine(Path.GetTempPath(), $"macro-deck-spawn-{Guid.NewGuid():N}");

		var result = await runner.RunAsync("touch", [spawned], TimeSpan.FromSeconds(5), CancellationToken.None);

		Assert.That(result.Started || File.Exists(spawned), Is.False);
	}
}
