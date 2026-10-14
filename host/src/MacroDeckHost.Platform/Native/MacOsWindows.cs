using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("macos")]
internal static class MacOsWindows
{
	private const string ApplicationServices = MacOsAccessibility.ApplicationServices;
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const uint WindowListOptionOnScreenOnly = 1; // kCGWindowListOptionOnScreenOnly
	private const int CfNumberSInt32Type = 3; // kCFNumberSInt32Type

	public static int? GetForegroundOwnerPid()
	{
		var layerKey = MacOsAccessibility.ReadGlobalRef(ApplicationServices, "kCGWindowLayer");
		var pidKey = MacOsAccessibility.ReadGlobalRef(ApplicationServices, "kCGWindowOwnerPID");
		if (layerKey == IntPtr.Zero || pidKey == IntPtr.Zero)
		{
			return null;
		}

		var windows = CGWindowListCopyWindowInfo(WindowListOptionOnScreenOnly, 0);
		if (windows == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var count = CFArrayGetCount(windows);
			for (nint i = 0; i < count; i++)
			{
				var window = CFArrayGetValueAtIndex(windows, i);
				if (window == IntPtr.Zero || !TryReadCfInt(window, layerKey, out var layer) || layer != 0)
				{
					continue;
				}

				if (TryReadCfInt(window, pidKey, out var pid))
				{
					return pid;
				}
			}
		}
		finally
		{
			CFRelease(windows);
		}

		return null;
	}

	public static bool TryReadCfInt(IntPtr dictionary, IntPtr key, out int value)
	{
		value = 0;
		var number = CFDictionaryGetValue(dictionary, key);
		return number != IntPtr.Zero && CFNumberGetValue(number, CfNumberSInt32Type, out value);
	}

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr handle);

	[DllImport(CoreFoundation)]
	private static extern nint CFArrayGetCount(IntPtr array);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
}
