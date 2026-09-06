namespace MacroDeckHost.Integrations.Keyboard.Native;

public sealed class NullKeyboardInputProvider : IKeyboardInputProvider
{
	private readonly string _reason;

	public NullKeyboardInputProvider(string reason)
	{
		_reason = reason;
	}

	public string PlatformName => $"unsupported ({_reason})";

	public bool IsSupported => false;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public void KeyDown(KeyCode key)
	{
	}

	public void KeyUp(KeyCode key)
	{
	}

	public void TypeUnicode(string text)
	{
	}

	public bool SupportsWindowTargeting => false;

	public bool SupportsBackgroundSend => false;

	public string? GetForegroundProcessName() => null;

	public IKeyboardTargetWindow? ResolveTarget(string processName) => null;
}
