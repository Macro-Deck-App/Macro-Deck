namespace MacroDeckHost.Platform.Lock;

public static class LockStateReaderFactory
{
	public static ILockStateReader Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsLockStateReader();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsLockStateReader();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxLockStateReader();
		}

		return new NullLockStateReader();
	}
}
