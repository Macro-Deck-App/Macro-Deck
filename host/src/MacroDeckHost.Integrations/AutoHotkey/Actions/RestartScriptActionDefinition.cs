using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.Restart;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class RestartScriptActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public RestartScriptActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.RestartId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Scripts;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
		[.. AutoHotkeyParameters.Targeted(), AutoHotkeyParameters.KeyValues()];

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
			if (AutoHotkeyActions.TargetRequest(context.Parameters) is not { } target)
			{
				return Task.FromResult(AutoHotkeyActions.MissingTarget(null));
			}

			var request = target with
			{
				Parameters = AutoHotkeyActionValues.ReadKeyValues(context.Parameters, AutoHotkeyParameters.Parameters),
				Policy = AutoHotkeyRunPolicy.Restart,
				VariableContext = UserVariableContext.From(context)
			};
			return AutoHotkeyActions.RunAsync(_host, request, context);
		}
	}
}
