using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal sealed class ReleaseAllButtonsActionDefinition : IActionDefinition
{
	private readonly IMouseInputService _input;

	public ReleaseAllButtonsActionDefinition(IMouseInputService input)
	{
		_input = input;
	}

	public string Id => "release-all-buttons";
	public LocalizedText Name => AppStrings.Integrations.Mouse.Actions.ReleaseAll.Name();

	public LocalizedText Description => AppStrings.Integrations.Mouse.Actions.ReleaseAll.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

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
			await _input.ReleaseAllAsync(context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
