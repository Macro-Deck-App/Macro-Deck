using System.Diagnostics;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Processes;
using Serilog;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class RunCommandActionDefinition : IActionDefinition
{
	private readonly VariableApiAccessor _variables;

	public RunCommandActionDefinition(VariableApiAccessor variables)
	{
		_variables = variables;
	}

	public string Id => "run-command";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Programs;
	public LocalizedText Name => AppStrings.Integrations.System.Actions.RunCommand.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.RunCommand.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice("shell",
			[
				new ActionParameterOption
				{
					Value = "default", Label = AppStrings.Integrations.System.Actions.RunCommand.ShellDefault()
				},
				new ActionParameterOption
				{
					Value = "powershell", Label = AppStrings.Integrations.System.Actions.RunCommand.ShellPowerShell()
				},
				new ActionParameterOption
				{
					Value = "bash", Label = AppStrings.Integrations.System.Actions.RunCommand.ShellBash()
				}
			],
			AppStrings.Integrations.System.Actions.RunCommand.ShellLabel(),
			defaultValue: "default"),
		ActionParameter.MultilineText("command",
			AppStrings.Integrations.System.Actions.RunCommand.CommandLabel(),
			AppStrings.Integrations.System.Actions.SupportsVariablesHint("{{ vars.name }}"),
			required: true),
		ActionParameter.Folder("workingDirectory",
			AppStrings.Integrations.System.Actions.WorkingDirectoryLabel()),
		ActionParameter.Toggle("showWindow",
			AppStrings.Integrations.System.Actions.RunCommand.ShowWindowLabel(),
			defaultValue: false),
		ActionParameter.Autocomplete("outputVariable",
			AppStrings.Integrations.System.Actions.RunCommand.OutputVariableLabel(),
			AppStrings.Integrations.System.Actions.RunCommand.OutputVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables),
		ActionParameter.Number("timeout",
			AppStrings.Integrations.System.Actions.RunCommand.TimeoutLabel(),
			AppStrings.Integrations.System.Actions.RunCommand.TimeoutDescription(),
			0,
			300,
			defaultValue: 30)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_variables);
	}

	private static readonly Lazy<bool> _isPwshInstalled = new(CheckPwshInstalled);

	private static bool CheckPwshInstalled()
	{
		try
		{
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = "pwsh",
				Arguments = "-Version",
				UseShellExecute = false,
				CreateNoWindow = true
			});
			if (process is not null)
			{
				if (!process.WaitForExit(TimeSpan.FromSeconds(2)))
				{
					try
					{
						process.Kill();
					}
					catch
					{
					}

					return false;
				}

				return process.ExitCode == 0;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	internal static (string FileName, string SwitchArgument, bool UseArgumentsString) ResolveShell(
		string shell,
		bool isWindows,
		bool? isPwshInstalled = null)
	{
		return (shell, isWindows) switch
		{
			("powershell", true) => isPwshInstalled ?? _isPwshInstalled.Value
				? ("pwsh", "-Command", false)
				: ("powershell.exe", "-Command", false),
			("powershell", false) => ("pwsh", "-Command", false),
			("bash", _) => ("bash", "-c", false),
			(_, true) => ("cmd.exe", "/s /c", true),
			(_, _) => ("sh", "-c", false)
		};
	}

	internal static bool IsOnPath(string fileName)
	{
		if (fileName.Contains(Path.DirectorySeparatorChar))
		{
			return IsExecutable(fileName);
		}

		var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Any(directory => IsExecutable(Path.Combine(directory, fileName)));
	}

	private static bool IsExecutable(string path)
	{
		if (!File.Exists(path))
		{
			return false;
		}

		if (OperatingSystem.IsWindows())
		{
			return true;
		}

		const UnixFileMode anyExecute
			= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
		return (File.GetUnixFileMode(path) & anyExecute) != 0;
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<RunCommandActionDefinition>(SystemIntegration.IntegrationId);

		private readonly VariableApiAccessor _variables;

		public Executor(VariableApiAccessor variables)
		{
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var command = SystemActionValues.ReadString(context.Parameters, "command");
			if (string.IsNullOrWhiteSpace(command))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Errors.NoCommandConfigured());
			}

			var shell = SystemActionValues.ReadString(context.Parameters, "shell");
			var workingDirectory = SystemActionValues.ReadString(context.Parameters, "workingDirectory");
			var showWindow = SystemActionValues.ReadBool(context.Parameters, "showWindow", false);
			var outputVariable = SystemActionValues.ReadString(context.Parameters, "outputVariable");
			var timeout = SystemActionValues.ReadInt(context.Parameters, "timeout", 30);
			var capture = !string.IsNullOrWhiteSpace(outputVariable);

			var (fileName, switchArgument, useArgumentsString) = ResolveShell(shell, OperatingSystem.IsWindows());
			var detach = !capture && !OperatingSystem.IsWindows();

			if (detach && !IsOnPath(fileName))
			{
				_logger.Warning("Shell {Shell} was not found for command: {Command}", fileName, command);
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.System.Errors.CommandCouldNotRun());
			}

			var startInfo = new ProcessStartInfo(fileName)
			{
				UseShellExecute = !capture && showWindow,
				CreateNoWindow = !showWindow,
				RedirectStandardOutput = capture,
				RedirectStandardError = capture
			};

			if (useArgumentsString)
			{
				startInfo.Arguments = $"{switchArgument} \"{command}\"";
			}
			else
			{
				startInfo.ArgumentList.Add(switchArgument);
				startInfo.ArgumentList.Add(command);
			}

			if (!string.IsNullOrWhiteSpace(workingDirectory))
			{
				startInfo.WorkingDirectory = workingDirectory;
			}

			AppImageEnvironment.RestoreOutside(startInfo);

			Process? process = null;
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
			if (timeout > 0)
			{
				cts.CancelAfter(TimeSpan.FromSeconds(timeout));
			}

			try
			{
				if (detach && !OperatingSystem.IsWindows())
				{
					return ExitResult(await DetachedProcess.Spawn(startInfo).WaitAsync(cts.Token));
				}

				process = Process.Start(startInfo);
				if (process is null)
				{
					return ActionResult.Failed("PROCESS_START_FAILED",
						AppStrings.Integrations.System.Errors.CommandStartFailed());
				}

				var output = string.Empty;
				var error = string.Empty;
				if (capture)
				{
					var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
					var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
					await Task.WhenAll(outputTask, errorTask);
					output = outputTask.Result;
					error = errorTask.Result;
				}

				await process.WaitForExitAsync(cts.Token);

				if (capture)
				{
					await ActionVariableTarget.WriteAsync(_variables.UserVariables,
						_variables.Current,
						outputVariable!,
						UserVariableContext.From(context),
						VariableType.Text,
						output,
						_logger);
				}

				if (!string.IsNullOrEmpty(error))
				{
					_logger.Warning("Command '{Command}' wrote to stderr: {Error}", command, error);
				}

				return ExitResult(process.ExitCode);
			}
			catch (OperationCanceledException) when (!capture)
			{
				if (context.CancellationToken.IsCancellationRequested)
				{
					throw;
				}

				_logger.Information("Command is still running after {Timeout}s and is left running: {Command}",
					timeout,
					command);
				return ActionResult.Success();
			}
			catch (OperationCanceledException)
			{
				_logger.Warning("Command timed out or was cancelled: {Command}", command);
				TryKill(process);

				if (context.CancellationToken.IsCancellationRequested)
				{
					throw;
				}

				return ActionResult.Failed(ActionErrorCodes.Timeout,
					AppStrings.Integrations.System.Errors.CommandTimedOut());
			}
			catch (Exception exception)
			{
				_logger.Warning(exception, "Failed to run command: {Command}", command);
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.System.Errors.CommandCouldNotRun());
			}
		}

		private static ActionResult ExitResult(int exitCode)
		{
			return exitCode == 0
				? ActionResult.Success()
				: ActionResult.Failed("EXIT_CODE", AppStrings.Integrations.System.Errors.CommandExitCode(exitCode));
		}

		private static void TryKill(Process? process)
		{
			try
			{
				if (process is { HasExited: false })
				{
					process.Kill(true);
				}
			}
			catch
			{
			}
		}
	}
}
