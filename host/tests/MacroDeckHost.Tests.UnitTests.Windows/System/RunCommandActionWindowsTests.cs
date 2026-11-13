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

	[TestCase("echo one\necho two")]
	[TestCase("echo one\r\necho two\r\n")]
	[TestCase("\r\necho one\r\n\r\necho two")]
	public async Task RunCommand_default_shell_runs_every_line_whatever_the_line_breaks(string command)
	{
		var (_, output) = await RunAsync("default", command);

		Assert.That(Lines(output), Is.EqualTo(_twoLines));
	}

	[TestCase("echo one\r\nexit /b 3")]
	[TestCase("echo one\r\ncmd /c exit 3")]
	public async Task RunCommand_default_shell_reports_the_exit_code_of_a_multi_line_command(string command)
	{
		var (result, _) = await RunAsync("default", command);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(result.ErrorMessage.Localized?.Arguments["code"], Is.EqualTo(3));
		});
	}

	[Test]
	public async Task RunCommand_default_shell_runs_a_block_that_spans_several_lines()
	{
		var (_, output) = await RunAsync("default", "if 1==1 (\r\n  echo inside\r\n)");

		Assert.That(output?.Trim(), Is.EqualTo("inside"));
	}

	[Test]
	public async Task RunCommand_default_shell_keeps_command_line_syntax_for_one_line_with_a_trailing_break()
	{
		var (_, output) = await RunAsync("default", "for %i in (x) do @echo %i\r\n");

		Assert.That(output?.Trim(), Is.EqualTo("x"));
	}

	[Test]
	public async Task RunCommand_default_shell_leaves_no_script_file_behind()
	{
		var (_, output) = await RunAsync("default", "echo %~f0\r\necho done");
		var scriptPath = Lines(output)?.FirstOrDefault();

		Assert.That(scriptPath, Is.Not.Null.And.Not.Empty);
		Assert.That(() => File.Exists(scriptPath), Is.False.After(5000, 50));
	}

	[TestCase("Grüße.txt")]
	[TestCase("日本.txt")]
	public async Task RunCommand_default_shell_keeps_every_character_of_a_multi_line_command(string fileName)
	{
		var directory = Directory.CreateTempSubdirectory("macrodeck-run-command-test-");
		try
		{
			var action = new RunCommandActionDefinition(new VariableApiAccessor());

			var result = await action.CreateExecutor()
				.ExecuteAsync(new ActionExecutionContext
				{
					Parameters = new Dictionary<string, object>
					{
						["shell"] = "default",
						["command"] = $"echo one\r\ntype nul > \"{fileName}\"",
						["workingDirectory"] = directory.FullName,
						["timeout"] = 60
					}
				});

			Assert.Multiple(() =>
			{
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(File.Exists(Path.Combine(directory.FullName, fileName)), Is.True);
			});
		}
		finally
		{
			directory.Delete(true);
		}
	}

	[TestCase("default", "echo Grüße")]
	[TestCase("default", "echo x\r\necho Grüße")]
	[TestCase("powershell", "Write-Output 'Grüße'")]
	[TestCase("powershell", "[Console]::OutputEncoding = [Text.Encoding]::UTF8; Write-Output 'Grüße'")]
	public async Task RunCommand_writes_non_ascii_output_to_the_variable_unchanged(string shell, string command)
	{
		var (_, output) = await RunAsync(shell, command);

		Assert.That(Lines(output)?.LastOrDefault(), Is.EqualTo("Grüße"));
	}

	private static string[]? Lines(string? output)
	{
		return output?.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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
