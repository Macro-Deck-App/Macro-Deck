using MacroDeckHost.Integrations.System.Application;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class OpenFileActionDefinition : IActionDefinition
{
	private readonly IApplicationService _applications;

	public OpenFileActionDefinition(IApplicationService applications)
	{
		_applications = applications;
	}

	public string Id => "open-file";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.OpenFile.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.OpenFile.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.File("path",
			label: AppStrings.Integrations.System.Actions.OpenFile.PathLabel(),
			required: true)
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
			var path = SystemActionValues.ReadString(context.Parameters, "path");
			_applications.OpenFile(path);
			return ActionResult.SucceededTask;
		}
	}
}
