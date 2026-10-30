using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ProcessRunnerTests
{
	private static readonly TimeSpan _childTimeout = TimeSpan.FromSeconds(60);

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunWithResultAsync_captures_non_zero_exit_code_and_stderr()
	{
		var result = await ProcessRunner.RunWithResultAsync("sh", ["-c", "echo oops 1>&2; exit 3"]);

		Assert.Multiple(() =>
		{
			Assert.That(result.ExitCode, Is.EqualTo(3));
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.StandardError, Does.Contain("oops"));
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunWithResultAsync_captures_stdout_and_success_on_zero_exit()
	{
		var result = await ProcessRunner.RunWithResultAsync("sh", ["-c", "echo hello"]);

		Assert.Multiple(() =>
		{
			Assert.That(result.ExitCode, Is.EqualTo(0));
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.StandardOutput, Does.Contain("hello"));
		});
	}

	[Test]
	[Platform("Win")]
	public async Task RunWithResultAsync_captures_non_zero_exit_code_and_stderr_on_windows()
	{
		var result = await ProcessRunner.RunWithResultAsync("cmd.exe",
			["/c", "echo oops 1>&2 & exit 3"]);

		Assert.Multiple(() =>
		{
			Assert.That(result.ExitCode, Is.EqualTo(3));
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.StandardError, Does.Contain("oops"));
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public void A_cancelled_run_kills_the_hung_process_instead_of_waiting_for_it()
	{
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
		var stopwatch = Stopwatch.StartNew();

		Assert.That(async () => await ProcessRunner.RunWithResultAsync("sleep", ["30"], null, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
	}

	[Test]
	public async Task A_child_that_writes_a_lot_to_stderr_still_completes()
	{
		var (fileName, arguments) = OperatingSystem.IsWindows()
			? ("powershell.exe",
				new[]
				{
					"-NoProfile", "-NonInteractive", "-Command",
					"[Console]::Error.Write([string]::new('x', 2000000)); [Console]::Out.Write('done')"
				})
			: ("sh", new[] { "-c", "head -c 2000000 /dev/zero | tr '\\000' x >&2; printf done" });

		var output = await ProcessRunner.RunAsync(fileName, arguments).WaitAsync(_childTimeout);

		Assert.That(output.Trim(), Is.EqualTo("done"));
	}

	[Test]
	public async Task Cancelling_a_run_ends_the_child()
	{
		var pidFile = Path.Combine(Path.GetTempPath(), $"md-process-runner-{Guid.NewGuid():N}.pid");
		var (fileName, arguments) = OperatingSystem.IsWindows()
			? ("powershell.exe",
				new[]
				{
					"-NoProfile", "-NonInteractive", "-Command",
					$"$PID | Out-File -Encoding ascii '{pidFile}'; Start-Sleep -Seconds 120"
				})
			: ("sh", new[] { "-c", $"echo $$ > '{pidFile}'; exec sleep 120" });
		using var cancellation = new CancellationTokenSource();
		try
		{
			var run = ProcessRunner.RunAsync(fileName, arguments, cancellation.Token);
			var pid = await ReadPidAsync(pidFile);

			await cancellation.CancelAsync();

			Assert.That(async () => await run.WaitAsync(_childTimeout),
				Throws.InstanceOf<OperationCanceledException>());
			Assert.That(await HasExitedAsync(pid), Is.True, "the child outlived the cancelled run");
		}
		finally
		{
			File.Delete(pidFile);
		}
	}

	private static async Task<int> ReadPidAsync(string pidFile)
	{
		using var deadline = new CancellationTokenSource(_childTimeout);
		while (true)
		{
			try
			{
				var text = (await File.ReadAllTextAsync(pidFile, deadline.Token)).Trim();
				if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
				{
					return pid;
				}
			}
			catch (IOException)
			{
			}

			await Task.Delay(50, deadline.Token);
		}
	}

	private static async Task<bool> HasExitedAsync(int pid)
	{
		Process process;
		try
		{
			process = Process.GetProcessById(pid);
		}
		catch (ArgumentException)
		{
			return true;
		}

		using (process)
		{
			using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try
			{
				await process.WaitForExitAsync(deadline.Token);
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}
	}
}
