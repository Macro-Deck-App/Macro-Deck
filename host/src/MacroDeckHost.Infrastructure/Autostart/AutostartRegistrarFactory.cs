namespace MacroDeckHost.Infrastructure.Autostart;

public static class AutostartRegistrarFactory
{
	public static IAutostartRegistrar Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsAutostartRegistrar();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsAutostartRegistrar(MacOsAutostartRegistrar.DefaultDirectory());
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxAutostartRegistrar(LinuxAutostartRegistrar.DefaultDirectory());
		}

		return new NullAutostartRegistrar();
	}
}
