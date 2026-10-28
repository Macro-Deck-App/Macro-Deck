using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.RunScript;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class RunScriptActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public RunScriptActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.RunScriptId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Scripts;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
		[AutoHotkeyParameters.ScriptChoice(), .. AutoHotkeyParameters.RunOptions()];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_host);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return AutoHotkeyActions.ScriptOptionsAsync(_host, context);
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
			if (AutoHotkeyActionValues.ReadGuid(context.Parameters, AutoHotkeyParameters.Script) is not { } scriptId)
			{
				return Task.FromResult(AutoHotkeyActions.MissingTarget(null));
			}

			var request = new AutoHotkeyRunRequest { Kind = AutoHotkeyTargetKind.Managed, ScriptId = scriptId };
			return AutoHotkeyActions.RunAsync(_host, AutoHotkeyActions.WithRunOptions(request, context), context);
		}
	}
}
