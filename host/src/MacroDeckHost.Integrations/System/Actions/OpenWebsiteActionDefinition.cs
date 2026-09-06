using MacroDeckHost.Integrations.System.Application;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class OpenWebsiteActionDefinition : IActionDefinition
{
	private readonly IApplicationService _applications;

	public OpenWebsiteActionDefinition(IApplicationService applications)
	{
		_applications = applications;
	}

	public string Id => "open-website";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.OpenWebsite.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.OpenWebsite.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Url("url",
			label: AppStrings.Integrations.System.Actions.OpenWebsite.UrlLabel(),
			description: AppStrings.Integrations.System.Actions.SupportsVariablesHint(),
			placeholder: "https://example.com",
			required: true,
			autoPrefixHttps: true)
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
			var url = SystemActionValues.ReadString(context.Parameters, "url");
			_applications.OpenWebsite(url);
			return ActionResult.SucceededTask;
		}
	}
}
