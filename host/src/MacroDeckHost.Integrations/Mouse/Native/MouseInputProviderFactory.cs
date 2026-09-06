namespace MacroDeckHost.Integrations.Mouse.Native;

public static class MouseInputProviderFactory
{
	public static IMouseInputProvider Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsMouseInputProvider();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsMouseInputProvider();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxMouseInputProvider();
		}

		return new NullMouseInputProvider("operating system not supported");
	}
}
