using MacroDeckHost.Integrations.System;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ProcessRunnerTests
{
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
}
