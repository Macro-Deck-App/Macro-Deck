using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

/// <summary>
/// The dynamic-options action of the three: <see cref="Parameters"/> declares its one field with no
/// options attached, and <see cref="GetDynamicOptionsAsync"/> supplies them itself rather than through
/// a named options source - see <see cref="IDynamicOptionsActionDefinition"/>. Forces the condition
/// the next synthetic reading reports, so a demo deck can show every icon the Weather widget knows
/// without waiting on the sine wave to get there.
/// </summary>
internal sealed class SetConditionAction(WellBehavedIntegration integration)
	: IActionDefinition, IDynamicOptionsActionDefinition
{
	public string Id => "set-condition";

	public LocalizedText Name => "Force weather condition";

	public LocalizedText Description => "Overrides the condition the next synthetic reading reports.";

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("condition", label: "Condition", required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(new DynamicOptionsResult
		{
			Options =
			[
				.. WellBehavedIntegration.SelectableConditions
					.Select(condition => new ActionParameterOption
						{ Value = condition.ToString(), Label = condition.ToString() })
			]
		});

	private sealed class Executor(WellBehavedIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (context.Parameters.GetValueOrDefault("condition") is not string text ||
				!Enum.TryParse<WeatherCondition>(text, out var condition))
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					"condition must be a known WeatherCondition name."));
			}

			integration.Station.SetForcedCondition(condition);
			return ActionResult.SucceededTask;
		}
	}
}
