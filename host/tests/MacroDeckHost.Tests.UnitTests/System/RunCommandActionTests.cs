using MacroDeckHost.Integrations.System.Actions;
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
		var api = new RecordingVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = api });

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

		var handle = await api.GetByNameAsync("result");
		Assert.That(handle, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(api.CreateCount, Is.EqualTo(1));
			Assert.That(handle!.Value?.ToString()?.Trim(), Is.EqualTo("hello"));
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
	public async Task RunCommand_a_command_that_outlives_its_timeout_reports_TIMEOUT()
	{
		var api = new RecordingVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = api });

		var result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = "sleep 5",
				["timeout"] = 1
			}
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
		});
	}
}
