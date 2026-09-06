namespace MacroDeckHost.Integrations.System.Notifications;

public static class NotificationServiceFactory
{
	public static INotificationService Create()
		=> new ShellFirstNotificationService(ShellNotificationBridge.Instance, CreatePlatform());

	internal static INotificationService CreatePlatform()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsNotificationService();
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
