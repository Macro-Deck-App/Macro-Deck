using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class ButtonDownActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public ButtonDownActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "button-down";
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.ButtonDown.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.ButtonDown.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		MouseParameters.Button,
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
			var button = MouseActionValues.ReadButton(context.Parameters);
			var target = MouseActionValues.ReadTarget(context.Parameters);

			await _input.ButtonDownAsync(button, target, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
