using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class ScrollActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public ScrollActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "scroll";
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.Scroll.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.Scroll.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice("direction",
			options:
			[
				new ActionParameterOption { Value = "up", Label = AppStrings.Integrations.Mouse.Actions.Scroll.Up() },
				new ActionParameterOption
					{ Value = "down", Label = AppStrings.Integrations.Mouse.Actions.Scroll.Down() },
				new ActionParameterOption
					{ Value = "left", Label = AppStrings.Integrations.Mouse.Actions.Scroll.Left() },
				new ActionParameterOption
					{ Value = "right", Label = AppStrings.Integrations.Mouse.Actions.Scroll.Right() }
			],
			label: AppStrings.Integrations.Mouse.Actions.Scroll.DirectionLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Scroll.DirectionDescription(),
			defaultValue: "down"),
		ActionParameter.Number("amount",
			label: AppStrings.Integrations.Mouse.Actions.Scroll.NotchesLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Scroll.NotchesDescription(),
			min: 1,
			max: 1_000,
			step: 1,
			defaultValue: 3),
		ActionParameter.Duration("stepDelay",
			label: AppStrings.Integrations.Mouse.Actions.Scroll.StepDelayLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Scroll.StepDelayDescription(),
			min: 0,
			max: 1_000,
			defaultMilliseconds: 0),
		.. MouseParameters.OptionalPosition
	];

	public IActionExecutor CreateExecutor() => new Executor(_input);

	private sealed class Executor : IActionExecutor
	{
		private readonly IMouseInputService _input;

		public Executor(IMouseInputService input)
		{
			_input = input;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (axis, sign) = MouseActionValues.ReadScrollDirection(context.Parameters);
			var amount = Math.Abs(MouseActionValues.ReadInt(context.Parameters, "amount", 3));
			var stepDelay = MouseActionValues.ReadInt(context.Parameters, "stepDelay", 0);
			var target = MouseActionValues.ReadTarget(context.Parameters);

			await _input.ScrollAsync(axis, amount * sign, target, stepDelay, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
