namespace MacroDeckHost.Integrations.Keyboard.Models;

public sealed class KeyboardSequence
{
	public IReadOnlyList<KeyboardStep> Steps { get; init; } = [];

	public int Repeat { get; init; } = 1;

	public int RepeatDelayMs { get; init; }
}
