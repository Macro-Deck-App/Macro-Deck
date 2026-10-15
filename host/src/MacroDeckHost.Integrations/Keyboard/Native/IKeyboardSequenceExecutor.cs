using MacroDeckHost.Integrations.Keyboard.Models;

namespace MacroDeckHost.Integrations.Keyboard.Native;

public interface IKeyboardSequenceExecutor
{
	Task<KeyboardSessionUnavailableReason?> ExecuteAsync(
		KeyboardSequence sequence,
		KeyboardTarget target = default,
		CancellationToken cancellationToken = default);
}
