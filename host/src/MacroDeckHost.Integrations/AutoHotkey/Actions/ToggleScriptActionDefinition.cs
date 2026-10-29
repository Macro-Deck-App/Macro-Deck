using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Errors;
using StateStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.States;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Actions.Toggle;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal sealed class ToggleScriptActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	public const string NotRunningState = "not-running";
	public const string RunningState = "running";

	public static readonly IReadOnlyList<ActionStateDefinition> States =
		ActionStates.Pair(NotRunningState, StateStrings.NotRunning(), RunningState, StateStrings.Running());

	private readonly AutoHotkeyActionHost _host;

	public ToggleScriptActionDefinition(AutoHotkeyActionHost host)
	{
		_host = host;
	}

	public string Id => AutoHotkeyActions.ToggleId;

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

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		bool? running = _host.Manager is { } manager
			&& AutoHotkeyActions.TargetRequest(parameters) is { } request
			&& AutoHotkeyActions.IdentityOf(request) is { } identity
				? manager.IsRunning(identity)
				: null;
		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(States, running));
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
			var target = AutoHotkeyActions.TargetRequest(context.Parameters);
			if (target is null || AutoHotkeyActions.IdentityOf(target) is not { } identity)
			{
				return AutoHotkeyActions.MissingTarget(target);
			}

			if (_host.Manager is not { } manager)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.NotInstalled());
			}

			if (manager.IsRunning(identity))
			{
				await manager.StopAsync(identity, true, context.CancellationToken).ConfigureAwait(false);
				return ActionResult.Success(NotRunningState);
			}

			var request = target with
			{
				Parameters = AutoHotkeyActionValues.ReadKeyValues(context.Parameters, AutoHotkeyParameters.Parameters),
				Policy = AutoHotkeyRunPolicy.Ignore,
				VariableContext = UserVariableContext.From(context)
			};
			return await AutoHotkeyActions.RunAsync(_host, request, context, RunningState).ConfigureAwait(false);
		}
	}
}
