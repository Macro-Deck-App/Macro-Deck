using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class DragActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public DragActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "drag";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Input;
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.Drag.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.Drag.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		MouseParameters.Button,
		ActionParameter.Choice("fromMode",
			[
				new ActionParameterOption
					{ Value = "current", Label = AppStrings.Integrations.Mouse.Actions.Drag.FromCurrent() },
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.Drag.FromAbsolute() }
			],
			AppStrings.Integrations.Mouse.Actions.Drag.StartLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.StartDescription(),
			"current"),
		MouseParameters.X("fromX",
				AppStrings.Integrations.Mouse.Actions.Drag.StartXLabel(),
				AppStrings.Integrations.Mouse.Actions.Drag.StartXDescription())
			.OnlyWhen("fromMode", "absolute"),
		MouseParameters.Y("fromY",
				AppStrings.Integrations.Mouse.Actions.Drag.StartYLabel(),
				AppStrings.Integrations.Mouse.Actions.Drag.StartYDescription())
			.OnlyWhen("fromMode", "absolute"),
		ActionParameter.Choice("toMode",
			[
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.Drag.ToAbsolute() },
				new ActionParameterOption
					{ Value = "relative", Label = AppStrings.Integrations.Mouse.Actions.Drag.ToRelative() }
			],
			AppStrings.Integrations.Mouse.Actions.Drag.EndLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.EndDescription(),
			"absolute"),
		MouseParameters.X("toX",
			AppStrings.Integrations.Mouse.Actions.Drag.EndXLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.EndXDescription()),
		MouseParameters.Y("toY",
			AppStrings.Integrations.Mouse.Actions.Drag.EndYLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.EndYDescription()),
		ActionParameter.Duration("duration",
			AppStrings.Integrations.Mouse.Actions.Drag.DurationLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.DurationDescription(),
			0,
			10_000,
			250),
		ActionParameter.Number("steps",
			AppStrings.Integrations.Mouse.Actions.Drag.StepsLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.StepsDescription(),
			1,
			200,
			1,
			20)
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
			var from = MouseActionValues.ReadTarget(context.Parameters, "fromMode", "fromX", "fromY");
			var to = MouseActionValues.ReadTarget(context.Parameters,
				"toMode",
				"toX",
				"toY",
				MouseCoordinateMode.Absolute);
			var duration = MouseActionValues.ReadInt(context.Parameters, "duration", 250);
			var steps = MouseActionValues.ReadInt(context.Parameters, "steps", 20);

			await _input.DragAsync(button, from, to, duration, steps, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
