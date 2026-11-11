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
		Apply(() => SetProcessDpiAwarenessContext(_perMonitorAwareV2),
			() => SetProcessDpiAwareness(ProcessPerMonitorDpiAware) == 0,
			SetProcessDpiAware);
	}

	// Windows Server Core images export none of these, and the host must still start there.
	internal static void Apply(Func<bool> perMonitorV2, Func<bool> perMonitor, Func<bool> systemAware)
	{
		foreach (var attempt in new[] { perMonitorV2, perMonitor, systemAware })
		{
			try
			{
				if (attempt())
				{
					return;
				}
			}
			catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
			{
			}
		}
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
