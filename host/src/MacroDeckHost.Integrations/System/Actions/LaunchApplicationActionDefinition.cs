using MacroDeckHost.Integrations.System.Application;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class LaunchApplicationActionDefinition : IActionDefinition
{
	private readonly IApplicationService _applications;

	public LaunchApplicationActionDefinition(IApplicationService applications)
	{
		_applications = applications;
	}

	public string Id => "launch-application";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.LaunchApplication.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.LaunchApplication.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = BuildParameters();

	public IActionExecutor CreateExecutor() => new Executor(_applications);

	private static List<ActionParameter> BuildParameters()
	{
		string[] pathExtensions = OperatingSystem.IsLinux()
			? ["exe", "lnk", "url", "app", "sh", "desktop"]
			: ["exe", "lnk", "url", "app", "sh"];

		var parameters = new List<ActionParameter>
		{
			ActionParameter.File("path",
				label: AppStrings.Integrations.System.Actions.LaunchApplication.PathLabel(),
				required: true,
				fileExtensions: pathExtensions),
			ActionParameter.Text("arguments",
				label: AppStrings.Integrations.System.Actions.LaunchApplication.ArgumentsLabel(),
				description: AppStrings.Integrations.System.Actions.SupportsVariablesHint()),
			ActionParameter.Folder("workingDirectory",
				label: AppStrings.Integrations.System.Actions.WorkingDirectoryLabel(),
				description: AppStrings.Integrations.System.Actions.LaunchApplication.WorkingDirectoryDescription()),
			ActionParameter.Choice("mode",
				options:
				[
					new ActionParameterOption
					{
						Value = "start", Label = AppStrings.Integrations.System.Actions.LaunchApplication.ModeStart()
					},
					new ActionParameterOption
					{
						Value = "start-stop",
						Label = AppStrings.Integrations.System.Actions.LaunchApplication.ModeStartStop()
					},
					new ActionParameterOption
					{
						Value = "start-focus",
						Label = AppStrings.Integrations.System.Actions.LaunchApplication.ModeStartFocus()
					}
				],
				label: AppStrings.Integrations.System.Actions.LaunchApplication.ModeLabel(),
				defaultValue: "start")
		};

		if (OperatingSystem.IsWindows())
		{
			parameters.Add(ActionParameter.Toggle("runAsAdmin",
				label: AppStrings.Integrations.System.Actions.LaunchApplication.RunAsAdminLabel(),
				description: AppStrings.Integrations.System.Actions.LaunchApplication.RunAsAdminDescription()));
		}

		return parameters;
	}

	internal static LaunchMode ParseMode(string mode) => mode switch
	{
		"start-stop" => LaunchMode.StartStop,
		"start-focus" => LaunchMode.StartFocus,
		_ => LaunchMode.Start
	};

	private sealed class Executor : IActionExecutor
	{
		private readonly IApplicationService _applications;

		public Executor(IApplicationService applications)
		{
			_applications = applications;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var path = SystemActionValues.ReadString(context.Parameters, "path");
			if (string.IsNullOrWhiteSpace(path))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Errors.NoApplicationConfigured());
			}

			var arguments = SystemActionValues.ReadString(context.Parameters, "arguments");
			var workingDirectory = SystemActionValues.ReadString(context.Parameters, "workingDirectory");
			var mode = ParseMode(SystemActionValues.ReadString(context.Parameters, "mode"));
			var runAsAdmin = SystemActionValues.ReadBool(context.Parameters, "runAsAdmin", false);

			await _applications.LaunchAsync(path,
				arguments,
				workingDirectory,
				mode,
				runAsAdmin,
				context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
