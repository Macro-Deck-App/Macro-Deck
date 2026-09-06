namespace MacroDeckHost.Integrations.System.Metrics;

public static class SystemMetricsServiceFactory
{
	public static ISystemMetricsService Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsSystemMetricsService();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsSystemMetricsService();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxSystemMetricsService();
		}

		return new NullSystemMetricsService();
	}
}
