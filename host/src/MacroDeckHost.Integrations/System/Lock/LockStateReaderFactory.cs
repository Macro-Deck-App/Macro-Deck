namespace MacroDeckHost.Integrations.System.Lock;

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

		return new NullLockStateReader();
	}
}
