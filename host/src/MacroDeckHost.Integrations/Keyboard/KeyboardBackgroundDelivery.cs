namespace MacroDeckHost.Integrations.Keyboard;

internal static class KeyboardBackgroundDelivery
{
	public static bool ModifiersCannotBeDelivered(
		IKeyboardInputService input,
		KeyboardTarget target,
		KeyModifier modifiers,
		KeyCode key)
	{
		if (!input.IsSupported || !target.HasProcess || target.Mode != KeyboardTargetMode.Background)
		{
			return false;
		}

		return (Required(modifiers, key) & ~input.BackgroundModifiers) != KeyModifier.None;
	}

	private static KeyModifier Required(KeyModifier modifiers, KeyCode key) => modifiers | AsModifier(key);

	private static KeyModifier AsModifier(KeyCode key) => key switch
	{
		KeyCode.LeftControl or KeyCode.RightControl => KeyModifier.Control,
		KeyCode.LeftShift or KeyCode.RightShift => KeyModifier.Shift,
		KeyCode.LeftAlt or KeyCode.RightAlt => KeyModifier.Alt,
		KeyCode.LeftMeta or KeyCode.RightMeta => KeyModifier.Meta,
		_ => KeyModifier.None
	};
}
