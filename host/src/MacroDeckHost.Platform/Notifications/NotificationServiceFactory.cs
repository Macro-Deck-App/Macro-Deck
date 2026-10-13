using Serilog;

namespace MacroDeckHost.Platform.Notifications;

public static class NotificationServiceFactory
{
	public static IPlatformNotificationService Create(ILogger logger)
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsNotificationService(logger);
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsNotificationService();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxNotificationService();
		}

		return new NullNotificationService();
	}
}
