namespace MacroDeckHost.Integrations.Keyboard.Native;

public static class KeyboardInputProviderFactory
{
	public static IKeyboardInputProvider Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsKeyboardInputProvider();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsKeyboardInputProvider();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxKeyboardInputProvider();
		}

		return new NullKeyboardInputProvider("operating system not supported");
	}
}
