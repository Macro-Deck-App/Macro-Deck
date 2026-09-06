using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal sealed class PressKeyActionDefinition : IActionDefinition
{
	private readonly IKeyboardInputService _input;
	private readonly IKeyboardLayoutService _layout;

	public PressKeyActionDefinition(IKeyboardInputService input, IKeyboardLayoutService layout)
	{
		_input = input;
		_layout = layout;
	}

	public string Id => "press-key";
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Actions.PressKeyName();
	public LocalizedText Description => AppStrings.Integrations.Keyboard.Actions.PressKeyDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.KeyboardCombo("combo",
			label: AppStrings.Integrations.Keyboard.Actions.PressKeyComboLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.PressKeyComboDescription(),
			required: true),
		ActionParameter.Number("repeat",
			label: AppStrings.Integrations.Keyboard.Actions.RepeatLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.RepeatDescription(),
			min: 1,
			max: 10_000,
			step: 1,
			defaultValue: 1),
		ActionParameter.Duration("repeatDelay",
			label: AppStrings.Integrations.Keyboard.Actions.RepeatDelayLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.RepeatDelayDescription(),
			min: 0,
			defaultMilliseconds: 0),
		.. KeyboardTargetParameters.All
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
			var (modifierNames, keyName) = KeyboardActionValues.ReadHotkey(context.Parameters, "combo");
			var modifiers = _layout.ResolveModifiers(modifierNames);
			_layout.TryResolveKey(keyName, out var key);

			if (key == KeyCode.None && modifiers == KeyModifier.None)
			{
				return ActionResult.Success();
			}

			var repeat = Math.Max(1, KeyboardActionValues.ReadInt(context.Parameters, "repeat", 1));
			var repeatDelay = Math.Max(0, KeyboardActionValues.ReadInt(context.Parameters, "repeatDelay", 0));
			var target = KeyboardActionValues.ReadTarget(context.Parameters);

			var result = await _input.OpenSessionAsync(target, context.CancellationToken).ConfigureAwait(false);
			if (result.Session is not { } sessionValue)
			{
				return KeyboardActionValues.SessionUnavailableResult(result.UnavailableReason);
			}

			using var session = sessionValue;
			await session.PressComboAsync(modifiers, key, repeat, repeatDelay, context.CancellationToken)
				.ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
