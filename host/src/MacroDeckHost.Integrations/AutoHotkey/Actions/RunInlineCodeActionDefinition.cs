using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.RunInlineCode;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class RunInlineCodeActionDefinition : IActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public RunInlineCodeActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.RunInlineCodeId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Scripts;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
		[AutoHotkeyParameters.CodeEditor(), .. AutoHotkeyParameters.RunOptions()];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_host);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly AutoHotkeyActionHost _host;

		public Executor(AutoHotkeyActionHost host)
		{
			_host = host;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var request = new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Inline,
				Code = context.Parameters.GetValueOrDefault(AutoHotkeyParameters.Code) as string
			};
			return AutoHotkeyActions.RunAsync(_host, AutoHotkeyActions.WithRunOptions(request, context), context);
		}
	}
}
