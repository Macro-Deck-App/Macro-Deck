using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal sealed class TypeTextActionDefinition : IActionDefinition
{
	private readonly IKeyboardInputService _input;

	public TypeTextActionDefinition(IKeyboardInputService input)
	{
		_input = input;
	}

	public string Id => "type-text";
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Actions.TypeTextName();
	public LocalizedText Description => AppStrings.Integrations.Keyboard.Actions.TypeTextDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.MultilineText("text",
			label: AppStrings.Integrations.Keyboard.Actions.TypeTextLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.TypeTextParameterDescription(),
			placeholder: AppStrings.Integrations.Keyboard.Actions.TypeTextPlaceholder(),
			required: true),
		.. KeyboardTargetParameters.All
	];

	public IActionExecutor CreateExecutor() => new Executor(_input);

	private sealed class Executor : IActionExecutor
	{
		private readonly IKeyboardInputService _input;

		public Executor(IKeyboardInputService input)
		{
			_input = input;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var text = KeyboardActionValues.ReadString(context.Parameters, "text");
			if (string.IsNullOrEmpty(text))
			{
				return ActionResult.Success();
			}

			var target = KeyboardActionValues.ReadTarget(context.Parameters);
			var result = await _input.OpenSessionAsync(target, context.CancellationToken).ConfigureAwait(false);
			if (result.Session is not { } sessionValue)
			{
				return KeyboardActionValues.SessionUnavailableResult(result.UnavailableReason);
			}

			using var session = sessionValue;
			await session.TypeTextAsync(text, context.CancellationToken).ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
