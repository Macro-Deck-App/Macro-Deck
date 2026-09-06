using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Scripts;
using MacroDeckHost.Integrations.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Scripts;

internal sealed class RunScriptActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<IScriptApi?> _scripts;

	public RunScriptActionDefinition(Func<IScriptApi?> scripts)
	{
		_scripts = scripts;
	}

	public string Id => "run-script";
	public LocalizedText Name => AppStrings.Integrations.Scripts.Actions.RunScriptName();
	public LocalizedText Description => AppStrings.Integrations.Scripts.Actions.RunScriptDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("scriptId",
			label: AppStrings.Integrations.Scripts.Actions.ScriptLabel(),
			description: AppStrings.Integrations.Scripts.Actions.ScriptDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_scripts);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var scripts = _scripts()?.GetScripts() ?? [];
		var options = scripts
			.Where(script => context.Filter is null ||
				script.Name.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			.Select(script => new ActionParameterOption
			{
				Value = script.Id,
				Label = script.Name,
				Metadata = ScriptInputParameters.Metadata(script.Inputs, script.RunsOnWidget)
			})
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 2
		});
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<IScriptApi?> _scripts;

		public Executor(Func<IScriptApi?> scripts)
		{
			_scripts = scripts;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var scripts = _scripts();
			if (scripts is null)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Scripts.Errors.ScriptsUnavailable()));
			}

			var scriptId = context.Parameters.TryGetValue("scriptId", out var value)
				? value.ToString() ?? string.Empty
				: string.Empty;

			if (string.IsNullOrEmpty(scriptId))
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Scripts.Errors.NoScriptSelected()));
			}

			var target = scripts.GetScripts().FirstOrDefault(s => s.Id == scriptId);
			var ownerWidgetId = target is { RunsOnWidget: true } ? WidgetActionParameters.TargetOf(context) : null;

			return scripts.RunAsync(scriptId,
				ScriptInputParameters.Collect(context.Parameters),
				context.OriginClientId,
				ownerWidgetId,
				context.CancellationToken);
		}
	}
}
