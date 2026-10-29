using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Errors;
using ParameterStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Parameters;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.Stop;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class StopScriptActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly AutoHotkeyActionHost _host;

	public StopScriptActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.StopId;

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Scripts;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		.. AutoHotkeyParameters.Targeted(),
		ActionParameter.Toggle(AutoHotkeyParameters.StopAll,
			ParameterStrings.StopAllLabel(),
			ParameterStrings.StopAllDescription(),
			true,
			true)
	];

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

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var request = AutoHotkeyActions.TargetRequest(context.Parameters);
			if (request is null || AutoHotkeyActions.IdentityOf(request) is not { } identity)
			{
				return AutoHotkeyActions.MissingTarget(request);
			}

			if (_host.Manager is not { } manager)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.NotInstalled());
			}

			var all = AutoHotkeyActionValues.ReadBool(context.Parameters, AutoHotkeyParameters.StopAll, true);
			await manager.StopAsync(identity, all, context.CancellationToken).ConfigureAwait(false);
			return ActionResult.Success();
		}
	}
}
