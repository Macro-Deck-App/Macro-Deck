using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class ButtonUpActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public ButtonUpActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "button-up";
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.ButtonUp.Name();
	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.ButtonUp.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [MouseParameters.Button];

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
			await _input.ButtonUpAsync(MouseActionValues.ReadButton(context.Parameters), context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
