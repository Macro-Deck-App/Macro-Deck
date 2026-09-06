namespace MacroDeckHost.Integrations.System.Application;

public static class ApplicationServiceFactory
{
	public static IApplicationService Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsApplicationService();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsApplicationService();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxApplicationService();
		}

		return new NullApplicationService();
	}
}
