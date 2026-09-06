namespace MacroDeckHost.Integrations.System.Volume;

public static class VolumeServiceFactory
{
	public static IVolumeService Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsVolumeService();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsVolumeService();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxVolumeService();
		}

		return new NullVolumeService();
	}
}
