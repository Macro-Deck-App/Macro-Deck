using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost;

public static class ProcessDpiAwareness
{
	private static readonly IntPtr _perMonitorAwareV2 = new(-4);

	private const int ProcessPerMonitorDpiAware = 2;

	public static void Configure()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		ConfigureWindows();
	}

	[SupportedOSPlatform("windows")]
	private static void ConfigureWindows()
	{
		try
		{
			if (SetProcessDpiAwarenessContext(_perMonitorAwareV2))
			{
				return;
			}
		}
		catch (EntryPointNotFoundException)
		{
		}

		try
		{
			if (SetProcessDpiAwareness(ProcessPerMonitorDpiAware) == 0)
			{
				return;
			}
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
		}

		_ = SetProcessDpiAware();
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

	[DllImport("shcore.dll")]
	private static extern int SetProcessDpiAwareness(int value);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetProcessDpiAware();
}
