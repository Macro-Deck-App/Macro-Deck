namespace MacroDeckHost.Integrations.Keyboard;

public interface IKeyboardLayoutService
{
	bool TryResolveKey(string name, out KeyCode key);

	bool TryResolveModifier(string name, out KeyModifier modifier);

	KeyModifier ResolveModifiers(IEnumerable<string> names);

	IReadOnlyList<KeyCode> ExpandModifiers(KeyModifier modifiers);
}
