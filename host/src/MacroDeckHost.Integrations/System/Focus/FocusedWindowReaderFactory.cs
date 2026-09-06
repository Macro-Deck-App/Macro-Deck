namespace MacroDeckHost.Integrations.System.Focus;

public static class FocusedWindowReaderFactory
{
	public static IFocusedWindowReader Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsFocusedWindowReader();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsFocusedWindowReader();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxFocusedWindowReader();
		}

		return new NullFocusedWindowReader();
	}
}
