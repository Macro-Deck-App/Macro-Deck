namespace MacroDeckHost.Integrations.System.Power;

public static class PowerServiceFactory
{
	public static IPowerService Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsPowerService();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsPowerService();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxPowerService();
		}

		return new NullPowerService();
	}
}
