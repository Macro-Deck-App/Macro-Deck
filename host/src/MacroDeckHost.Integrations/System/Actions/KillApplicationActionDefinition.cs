using MacroDeckHost.Integrations.System.Application;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class KillApplicationActionDefinition : IActionDefinition
{
	private readonly IApplicationService _applications;

	public KillApplicationActionDefinition(IApplicationService applications)
	{
		_applications = applications;
	}

	public string Id => "kill-application";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.KillApplication.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.KillApplication.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete("process",
			label: AppStrings.Integrations.System.Actions.KillApplication.ProcessLabel(),
			description: AppStrings.Integrations.System.Actions.KillApplication.ProcessDescription(),
			optionsSourceId: "system.processes"),
		ActionParameter.Toggle("graceful",
			label: AppStrings.Integrations.System.Actions.KillApplication.GracefulLabel(),
			description: AppStrings.Integrations.System.Actions.KillApplication.GracefulDescription(),
			defaultValue: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_applications);

	private sealed class Executor : IActionExecutor
	{
		private readonly IApplicationService _applications;

		public Executor(IApplicationService applications)
		{
			_applications = applications;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var process = SystemActionValues.ReadString(context.Parameters, "process");
			var graceful = SystemActionValues.ReadBool(context.Parameters, "graceful", true);
			_applications.Kill(process, graceful);
			return ActionResult.SucceededTask;
		}
	}
}
