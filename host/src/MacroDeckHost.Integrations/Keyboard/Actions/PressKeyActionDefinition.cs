using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Keyboard.Native;
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

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Input;
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Actions.PressKeyName();
	public LocalizedText Description => AppStrings.Integrations.Keyboard.Actions.PressKeyDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.KeyboardCombo("combo",
			AppStrings.Integrations.Keyboard.Actions.PressKeyComboLabel(),
			AppStrings.Integrations.Keyboard.Actions.PressKeyComboDescription(),
			true),
		ActionParameter.Number("repeat",
			AppStrings.Integrations.Keyboard.Actions.RepeatLabel(),
			AppStrings.Integrations.Keyboard.Actions.RepeatDescription(),
			1,
			10_000,
			1,
			1),
		ActionParameter.Duration("repeatDelay",
			AppStrings.Integrations.Keyboard.Actions.RepeatDelayLabel(),
			AppStrings.Integrations.Keyboard.Actions.RepeatDelayDescription(),
			0,
			defaultMilliseconds: 0),
		.. KeyboardTargetParameters.All
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_input, _layout);
	}

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
			var key = KeyboardActionValues.ResolveKey(_layout, keyName);

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
			if (KeyboardBackgroundDelivery.ModifiersCannotBeDelivered(_input, target, modifiers, key))
			{
				return KeyboardActionValues.SessionUnavailableResult(KeyboardSessionUnavailableReason
					.BackgroundModifiersUnsupported);
			}

			await session.PressComboAsync(modifiers, key, repeat, repeatDelay, context.CancellationToken)
				.ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
