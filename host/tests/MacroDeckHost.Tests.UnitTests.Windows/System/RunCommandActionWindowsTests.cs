using MacroDeckHost.Integrations.System.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class RunCommandActionWindowsTests
{
	private static readonly string[] _twoLines = ["one", "two"];

	[Test]
	public async Task RunCommand_cmd_exe_preserves_inner_quotes()
	{
		var userVariables = new RecordingUserVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { UserVariables = userVariables });

		await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = "default",
					["command"] = "\"cmd.exe\" /c echo \"hello quoted\"",
					["outputVariable"] = "result",
					["timeout"] = 30
				}
			});

		Assert.That(userVariables.Values.TryGetValue("result", out var value), Is.True);
		Assert.That(value?.Trim(), Is.EqualTo("\"hello quoted\""));
	}

	[Test]
	public async Task RunCommand_powershell_runs_in_pwsh_when_it_is_installed_and_in_windows_powershell_otherwise()
	{
		var pwshOnPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Any(directory => File.Exists(Path.Combine(directory, "pwsh.exe")));
		var expected = pwshOnPath ? "Core" : "Desktop";

		var (result, output) = await RunAsync("powershell", "$PSVersionTable.PSEdition");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(output?.Trim(), Is.EqualTo(expected));
		});
	}

	[TestCase("default", "exit /b 4")]
	[TestCase("powershell", "exit 4")]
	public async Task RunCommand_reports_the_exit_code_of_each_shell(string shell, string command)
	{
		var (result, _) = await RunAsync(shell, command);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(result.ErrorMessage.Localized?.Arguments["code"], Is.EqualTo(4));
		});
	}

	[TestCase("default")]
	[TestCase("powershell")]
	public async Task RunCommand_runs_every_line_of_a_multi_line_command(string shell)
	{
		var (_, output) = await RunAsync(shell, "echo one\r\necho two");

		Assert.That(output?.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
			Is.EqualTo(_twoLines));
	}

	private static async Task<(ActionResult Result, string? Output)> RunAsync(string shell, string command)
	{
		var userVariables = new RecordingUserVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { UserVariables = userVariables });

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					["shell"] = shell,
					["command"] = command,
					["outputVariable"] = "result",
					["timeout"] = 60
				}
			});

		return (result, userVariables.Values.GetValueOrDefault("result"));
	}

	private sealed class RecordingUserVariableApi : IUserVariableApi
	{
		public Dictionary<string, string?> Values { get; } = new();

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			if (!Values.ContainsKey(name))
			{
				return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound, name));
			}

			Values[name] = value;
			return Task.FromResult(UserVariableWriteResult.Applied());
		}

		public Task<UserVariableCreateResult> CreateAsync(
			string name,
			string? ownerWidgetId,
			VariableType type,
			string? initialValue = null,
			int? decimalPlaces = null,
			CancellationToken cancellationToken = default)
		{
			Values[name] = initialValue;
			return Task.FromResult(UserVariableCreateResult.Created());
		}
	}
}
