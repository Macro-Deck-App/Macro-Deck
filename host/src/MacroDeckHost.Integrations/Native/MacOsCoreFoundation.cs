using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Native;

[SupportedOSPlatform("macos")]
internal static class MacOsCoreFoundation
{
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const int CFNumberSInt32Type = 3; // kCFNumberSInt32Type

	public static bool TryReadCFBool(IntPtr dictionary, IntPtr key, out bool value)
	{
		value = false;
		var boxed = CFDictionaryGetValue(dictionary, key);
		if (boxed == IntPtr.Zero)
		{
			return false;
		}

		value = CFBooleanGetValue(boxed);
		return true;
	}

	public static bool TryReadCFInt(IntPtr dictionary, IntPtr key, out int value)
	{
		value = 0;
		var number = CFDictionaryGetValue(dictionary, key);
		return number != IntPtr.Zero && CFNumberGetValue(number, CFNumberSInt32Type, out value);
	}

	public static IntPtr CreateCFString(string value)
	{
		var bytes = MacOsAccessibility.Utf8(value);
		return CFStringCreateWithCString(IntPtr.Zero, bytes, 0x08000100); // kCFStringEncodingUTF8
	}

	[DllImport(CoreFoundation)]
	public static extern void CFRelease(IntPtr handle);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] bytes, uint encoding);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFBooleanGetValue(IntPtr booleanRef);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
}
