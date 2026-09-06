namespace MacroDeckHost.Integrations.Keyboard;

public enum KeyboardTargetMode
{
	WhenFocused,

	FocusThenSend,

	Background
}

public readonly record struct KeyboardTarget(string ProcessName, KeyboardTargetMode Mode)
{
	public static readonly KeyboardTarget None = new(string.Empty, KeyboardTargetMode.WhenFocused);

	public bool HasProcess => !string.IsNullOrWhiteSpace(ProcessName);
}
