namespace MacroDeckHost.Integrations.Keyboard.Native;

public sealed class KeyboardLayoutService : IKeyboardLayoutService
{
	public bool TryResolveKey(string name, out KeyCode key) => KeyNames.TryParseKey(name, out key);

	public bool TryResolveModifier(string name, out KeyModifier modifier) => KeyNames.TryParseModifier(name, out modifier);

	public KeyModifier ResolveModifiers(IEnumerable<string> names) => KeyNames.ResolveModifiers(names);

	public IReadOnlyList<KeyCode> ExpandModifiers(KeyModifier modifiers)
	{
		var result = new List<KeyCode>(8);
		if (modifiers.HasFlag(KeyModifier.Control))
		{
			result.Add(KeyCode.LeftControl);
		}

		if (modifiers.HasFlag(KeyModifier.RightControl))
		{
			result.Add(KeyCode.RightControl);
		}

		if (modifiers.HasFlag(KeyModifier.Shift))
		{
			result.Add(KeyCode.LeftShift);
		}

		if (modifiers.HasFlag(KeyModifier.RightShift))
		{
			result.Add(KeyCode.RightShift);
		}

		if (modifiers.HasFlag(KeyModifier.Alt))
		{
			result.Add(KeyCode.LeftAlt);
		}

		if (modifiers.HasFlag(KeyModifier.RightAlt))
		{
			result.Add(KeyCode.RightAlt);
		}

		if (modifiers.HasFlag(KeyModifier.Meta))
		{
			result.Add(KeyCode.LeftMeta);
		}

		if (modifiers.HasFlag(KeyModifier.RightMeta))
		{
			result.Add(KeyCode.RightMeta);
		}

		return result;
	}
}
