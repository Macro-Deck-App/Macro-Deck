namespace MacroDeckHost.Integrations.Keyboard;

public interface IKeyboardInputSession : IDisposable
{
	Task PressComboAsync(
		KeyModifier modifiers,
		KeyCode key,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default);

	Task TypeTextAsync(string text, CancellationToken cancellationToken = default);

	Task KeyDownAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default);

	Task KeyUpAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default);
}
