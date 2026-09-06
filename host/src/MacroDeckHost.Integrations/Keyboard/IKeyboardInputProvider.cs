namespace MacroDeckHost.Integrations.Keyboard;

public interface IKeyboardInputProvider
{
	string PlatformName { get; }

	bool IsSupported { get; }

	bool RequiresPermission { get; }

	bool HasPermission { get; }

	void RequestPermission();

	void KeyDown(KeyCode key);

	void KeyUp(KeyCode key);

	void TypeUnicode(string text);

	bool SupportsWindowTargeting { get; }

	bool SupportsBackgroundSend { get; }

	string? GetForegroundProcessName();

	IKeyboardTargetWindow? ResolveTarget(string processName);
}

public interface IKeyboardTargetWindow : IDisposable
{
	IDisposable? Focus();

	void KeyDown(KeyCode key);

	void KeyUp(KeyCode key);

	void TypeUnicode(string text);
}
