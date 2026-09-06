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
}
