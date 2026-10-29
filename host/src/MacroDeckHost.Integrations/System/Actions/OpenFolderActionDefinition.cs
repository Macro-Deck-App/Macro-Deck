using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class OpenFolderActionDefinition : IActionDefinition
{
	private readonly IApplicationService _applications;

	public OpenFolderActionDefinition(IApplicationService applications)
	{
		_applications = applications;
	}

	public string Id => "open-folder";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Files;
	public LocalizedText Name => AppStrings.Integrations.System.Actions.OpenFolder.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.OpenFolder.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Folder("path",
			AppStrings.Integrations.System.Actions.OpenFolder.PathLabel(),
			required: true)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_applications);
	}

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
			_applications.OpenFolder(path);
			return ActionResult.SucceededTask;
		}
	}
}
