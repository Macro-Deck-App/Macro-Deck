using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
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
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.Drag.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.Drag.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		MouseParameters.Button,
		ActionParameter.Choice("fromMode",
			options:
			[
				new ActionParameterOption
					{ Value = "current", Label = AppStrings.Integrations.Mouse.Actions.Drag.FromCurrent() },
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.Drag.FromAbsolute() }
			],
			label: AppStrings.Integrations.Mouse.Actions.Drag.StartLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Drag.StartDescription(),
			defaultValue: "current"),
		MouseParameters.X("fromX",
				AppStrings.Integrations.Mouse.Actions.Drag.StartXLabel(),
				AppStrings.Integrations.Mouse.Actions.Drag.StartXDescription())
			.OnlyWhen("fromMode", "absolute"),
		MouseParameters.Y("fromY",
				AppStrings.Integrations.Mouse.Actions.Drag.StartYLabel(),
				AppStrings.Integrations.Mouse.Actions.Drag.StartYDescription())
			.OnlyWhen("fromMode", "absolute"),
		ActionParameter.Choice("toMode",
			options:
			[
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.Drag.ToAbsolute() },
				new ActionParameterOption
					{ Value = "relative", Label = AppStrings.Integrations.Mouse.Actions.Drag.ToRelative() }
			],
			label: AppStrings.Integrations.Mouse.Actions.Drag.EndLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Drag.EndDescription(),
			defaultValue: "absolute"),
		MouseParameters.X("toX",
			AppStrings.Integrations.Mouse.Actions.Drag.EndXLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.EndXDescription()),
		MouseParameters.Y("toY",
			AppStrings.Integrations.Mouse.Actions.Drag.EndYLabel(),
			AppStrings.Integrations.Mouse.Actions.Drag.EndYDescription()),
		ActionParameter.Duration("duration",
			label: AppStrings.Integrations.Mouse.Actions.Drag.DurationLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Drag.DurationDescription(),
			min: 0,
			max: 10_000,
			defaultMilliseconds: 250),
		ActionParameter.Number("steps",
			label: AppStrings.Integrations.Mouse.Actions.Drag.StepsLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Drag.StepsDescription(),
			min: 1,
			max: 200,
			step: 1,
			defaultValue: 20)
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
