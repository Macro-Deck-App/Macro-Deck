using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class ClickActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public ClickActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "click";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Input;
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.Click.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.Click.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		MouseParameters.Button,
		ActionParameter.Choice("clickCount",
			[
				new ActionParameterOption
					{ Value = "1", Label = AppStrings.Integrations.Mouse.Actions.Click.SingleClick() },
				new ActionParameterOption
					{ Value = "2", Label = AppStrings.Integrations.Mouse.Actions.Click.DoubleClick() },
				new ActionParameterOption
					{ Value = "3", Label = AppStrings.Integrations.Mouse.Actions.Click.TripleClick() }
			],
			AppStrings.Integrations.Mouse.Actions.Click.ClickTypeLabel(),
			AppStrings.Integrations.Mouse.Actions.Click.ClickTypeDescription(),
			"1"),
		.. MouseParameters.OptionalPosition,
		ActionParameter.Number("repeat",
			AppStrings.Integrations.Mouse.Actions.Click.RepeatLabel(),
			AppStrings.Integrations.Mouse.Actions.Click.RepeatDescription(),
			1,
			10_000,
			1,
			1),
		ActionParameter.Duration("repeatDelay",
			AppStrings.Integrations.Mouse.Actions.Click.RepeatDelayLabel(),
			AppStrings.Integrations.Mouse.Actions.Click.RepeatDelayDescription(),
			0,
			defaultMilliseconds: 0)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_input);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly IMouseInputService _input;

		public Executor(IMouseInputService input)
		{
			_input = input;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var button = MouseActionValues.ReadButton(context.Parameters);
			var clickCount = MouseActionValues.ReadInt(context.Parameters, "clickCount", 1);
			var target = MouseActionValues.ReadTarget(context.Parameters);
			var repeat = MouseActionValues.ReadInt(context.Parameters, "repeat", 1);
			var repeatDelay = MouseActionValues.ReadInt(context.Parameters, "repeatDelay", 0);

			await _input.ClickAsync(button,
				clickCount,
				target,
				repeat,
				repeatDelay,
				context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
