using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Keyboard.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyComboStep), "keyCombo")]
[JsonDerivedType(typeof(TextStep), "text")]
[JsonDerivedType(typeof(DelayStep), "delay")]
[JsonDerivedType(typeof(KeyDownStep), "keyDown")]
[JsonDerivedType(typeof(KeyUpStep), "keyUp")]
public abstract class KeyboardStep;

public sealed class KeyComboStep : KeyboardStep
{
	public IReadOnlyList<string> Modifiers { get; init; } = [];

	public string Key { get; init; } = string.Empty;

	public int Repeat { get; init; } = 1;

	public int RepeatDelayMs { get; init; }
}

public sealed class TextStep : KeyboardStep
{
	public string Text { get; init; } = string.Empty;
}

public sealed class DelayStep : KeyboardStep
{
	public int Milliseconds { get; init; }
}

public sealed class KeyDownStep : KeyboardStep
{
	public IReadOnlyList<string> Modifiers { get; init; } = [];

	public string Key { get; init; } = string.Empty;
}

public sealed class KeyUpStep : KeyboardStep
{
	public IReadOnlyList<string> Modifiers { get; init; } = [];

	public string Key { get; init; } = string.Empty;
}
