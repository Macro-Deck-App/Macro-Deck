namespace MacroDeckHost.Platform.Applications;

internal static class ApplicationWindowReaderFactory
{
	public static IApplicationWindowReader Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsApplicationWindowReader();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsApplicationWindowReader();
		}

		if (OperatingSystem.IsLinux())
		{
			return new X11ApplicationWindowReader();
		}

		return new NullApplicationWindowReader();
	}
}
