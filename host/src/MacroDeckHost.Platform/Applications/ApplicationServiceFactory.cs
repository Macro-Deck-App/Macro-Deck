using Serilog;

namespace MacroDeckHost.Platform.Applications;

public static class ApplicationServiceFactory
{
	public static IApplicationService Create(ILogger logger)
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsApplicationService(logger);
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsApplicationService(logger);
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxApplicationService(logger);
		}

		return new NullApplicationService();
	}
}
