using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal sealed class KeyDownActionDefinition : IActionDefinition
{
	private readonly IKeyboardInputService _input;
	private readonly IKeyboardLayoutService _layout;

	public KeyDownActionDefinition(IKeyboardInputService input, IKeyboardLayoutService layout)
	{
		_input = input;
		_layout = layout;
	}

	public string Id => "key-down";
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Actions.KeyDownName();
	public LocalizedText Description => AppStrings.Integrations.Keyboard.Actions.KeyDownDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.KeyboardCombo("keys",
			label: AppStrings.Integrations.Keyboard.Actions.KeyParameterLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.KeyDownParameterDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_input, _layout);

	private sealed class Executor : IActionExecutor
	{
		private readonly IKeyboardInputService _input;
		private readonly IKeyboardLayoutService _layout;

		public Executor(IKeyboardInputService input, IKeyboardLayoutService layout)
		{
			_input = input;
			_layout = layout;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var (modifierNames, keyName) = KeyboardActionValues.ReadHotkey(context.Parameters, "keys");
			var modifiers = _layout.ResolveModifiers(modifierNames);
			_layout.TryResolveKey(keyName, out var key);
			await _input.KeyDownAsync(modifiers, key, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
