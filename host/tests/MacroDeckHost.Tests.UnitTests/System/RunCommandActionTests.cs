using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class RunCommandActionTests
{
	[TestCase("powershell", false, true, "pwsh", "-Command", false)]
	[TestCase("powershell", true, true, "pwsh", "-Command", false)]
	[TestCase("powershell", true, false, "powershell.exe", "-Command", false)]
	[TestCase("bash", true, null, "bash", "-c", false)]
	[TestCase("default", true, null, "cmd.exe", "/s /c", true)]
	[TestCase("default", false, null, "sh", "-c", false)]
	public void ResolveShell_maps_shell_and_platform(
		string shell,
		bool isWindows,
		bool? isPwshInstalled,
		string expectedFile,
		string expectedSwitch,
		bool expectedUseArgumentsString)
	{
		var (file, switchArgument, useArgumentsString) =
			RunCommandActionDefinition.ResolveShell(shell, isWindows, isPwshInstalled);

		Assert.Multiple(() =>
		{
			Assert.That(file, Is.EqualTo(expectedFile));
			Assert.That(switchArgument, Is.EqualTo(expectedSwitch));
			Assert.That(useArgumentsString, Is.EqualTo(expectedUseArgumentsString));
		});
	}

	[Test]
	public async Task RunCommand_captures_stdout_into_a_new_variable()
	{
		using var targets = new ActionVariableTargets(SystemIntegration.IntegrationId);
		var action = new RunCommandActionDefinition(new VariableApiAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		});

		await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = "echo hello",
				["outputVariable"] = "result",
				["timeout"] = 30
			}
		});

		var variable = await targets.Find("result");
		Assert.That(variable, Is.Not.Null);
		Assert.Multiple(async () =>
		{
			Assert.That(variable!.Classification, Is.EqualTo(VariableClassification.User));
			Assert.That((await targets.Service.GetAll()).Count(v => v.Name == "result"), Is.EqualTo(1));
			Assert.That((await targets.ValueOf("result"))?.ToString()?.Trim(), Is.EqualTo("hello"));
		});
	}

	[Test]
	public async Task RunCommand_ignores_blank_command()
	{
		var api = new RecordingVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = api });

		await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["command"] = "   ",
				["outputVariable"] = "result"
			}
		});

		Assert.That(api.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task RunCommand_reports_a_nonzero_exit_code_as_a_failure()
	{
		var api = new RecordingVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = api });

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = "exit 3",
				["timeout"] = 30
			}
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunCommand_without_output_capture_leaves_a_command_running_past_its_timeout()
	{
		using var pidFile = new PidFile();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = new RecordingVariableApi() });

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = pidFile.LongRunningCommand,
				["timeout"] = 1
			}
		});

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await pidFile.IsRunning(), Is.True);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunCommand_with_output_capture_stops_a_command_that_outlives_its_timeout()
	{
		using var pidFile = new PidFile();
		using var targets = new ActionVariableTargets(SystemIntegration.IntegrationId);
		var action = new RunCommandActionDefinition(new VariableApiAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		});

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = pidFile.LongRunningCommand,
				["outputVariable"] = "result",
				["timeout"] = 1
			}
		});

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(await pidFile.HasStopped(), Is.True);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunCommand_without_output_capture_leaves_the_command_running_when_the_run_is_cancelled()
	{
		using var pidFile = new PidFile();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = new RecordingVariableApi() });

		Assert.That(async () => await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = "default",
					["command"] = pidFile.LongRunningCommand,
					["timeout"] = 30
				},
				CancellationToken = cancellation.Token
			}),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(await pidFile.IsRunning(), Is.True);
	}

	[Test]
	[Platform(Exclude = "Win")]
	public async Task RunCommand_without_output_capture_does_not_hand_the_host_streams_to_the_command()
	{
		using (var inherits = Process.Start(new ProcessStartInfo("sh") { ArgumentList = { "-c", "[ /dev/stdout -ef /dev/null ]" } })!)
		{
			await inherits.WaitForExitAsync();
			Assume.That(inherits.ExitCode, Is.Not.EqualTo(0), "the test runner's own stdout is already /dev/null");
		}

		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = new RecordingVariableApi() });

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = "[ /dev/stdin -ef /dev/null ] && [ /dev/stdout -ef /dev/null ] && [ /dev/stderr -ef /dev/null ]",
				["timeout"] = 30
			}
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	[Platform(Exclude = "Win")]
	public void IsOnPath_finds_only_programs_that_exist()
	{
		Assert.Multiple(() =>
		{
			Assert.That(RunCommandActionDefinition.IsOnPath("sh"), Is.True);
			Assert.That(RunCommandActionDefinition.IsOnPath("macro-deck-no-such-shell"), Is.False);
			Assert.That(RunCommandActionDefinition.IsOnPath("/macro-deck/no/such/shell"), Is.False);
		});
	}

	[Test]
	[Platform(Exclude = "Win")]
	[UnsupportedOSPlatform("windows")]
	public void IsOnPath_does_not_accept_a_file_that_cannot_be_executed()
	{
		var path = Path.Combine(Path.GetTempPath(), $"not-a-shell-{Guid.NewGuid():N}");
		File.WriteAllText(path, "#!/bin/sh\n");
		try
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

			Assert.That(RunCommandActionDefinition.IsOnPath(path), Is.False);
		}
		finally
		{
			File.Delete(path);
		}
	}

	private sealed class PidFile : IDisposable
	{
		private readonly string _path = Path.Combine(Path.GetTempPath(), $"run-command-{Guid.NewGuid():N}.pid");

		public string LongRunningCommand => $"echo $$ > '{_path}'; exec sleep 60";

		public async Task<bool> IsRunning()
		{
			var process = await Find();
			return process is { HasExited: false };
		}

		public async Task<bool> HasStopped()
		{
			for (var attempt = 0; attempt < 30; attempt++)
			{
				if (!await IsRunning())
				{
					return true;
				}

				await Task.Delay(100);
			}

			return false;
		}

		public void Dispose()
		{
			try
			{
				Find().GetAwaiter().GetResult()?.Kill();
			}
			catch (InvalidOperationException)
			{
			}

			File.Delete(_path);
		}

		private async Task<Process?> Find()
		{
			for (var attempt = 0; attempt < 30 && !File.Exists(_path); attempt++)
			{
				await Task.Delay(100);
			}

			if (!File.Exists(_path) || !int.TryParse((await File.ReadAllTextAsync(_path)).Trim(), out var pid))
			{
				return null;
			}

			try
			{
				return Process.GetProcessById(pid);
			}
			catch (ArgumentException)
			{
				return null;
			}
		}
	}
}
