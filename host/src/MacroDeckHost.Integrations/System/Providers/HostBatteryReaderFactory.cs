namespace MacroDeckHost.Integrations.System.Providers;

internal static class HostBatteryReaderFactory
{
	public static IHostBatteryReader Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsHostBatteryReader();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsHostBatteryReader();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxHostBatteryReader();
		}

		return new NullHostBatteryReader();
	}
}
