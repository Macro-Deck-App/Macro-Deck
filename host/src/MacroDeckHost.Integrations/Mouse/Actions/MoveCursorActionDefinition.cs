using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Mouse.Models;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class MoveCursorActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public MoveCursorActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "move-cursor";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Input;
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.MoveCursor.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.MoveCursor.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice("positionMode",
			[
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.MoveCursor.Absolute() },
				new ActionParameterOption
					{ Value = "relative", Label = AppStrings.Integrations.Mouse.Actions.MoveCursor.Relative() }
			],
			AppStrings.Integrations.Mouse.Actions.MoveCursor.MoveLabel(),
			AppStrings.Integrations.Mouse.Actions.MoveCursor.MoveDescription(),
			"absolute"),
		MouseParameters.X("x",
			AppStrings.Integrations.Mouse.Actions.Shared.XLabel(),
			AppStrings.Integrations.Mouse.Actions.Shared.XDescription()),
		MouseParameters.Y("y",
			AppStrings.Integrations.Mouse.Actions.Shared.YLabel(),
			AppStrings.Integrations.Mouse.Actions.Shared.YDescription())
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
			var target = MouseActionValues.ReadTarget(context.Parameters,
				fallbackMode: MouseCoordinateMode.Absolute);

			await _input.MoveAsync(target, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
