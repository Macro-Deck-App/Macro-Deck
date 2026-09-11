using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

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

	private const uint CFStringEncodingUtf8 = 0x08000100;

	public static IntPtr CreateCFString(string value)
	{
		var bytes = MacOsAccessibility.Utf8(value);
		return CFStringCreateWithCString(IntPtr.Zero, bytes, CFStringEncodingUtf8);
	}

	public static string? ReadCFString(IntPtr value)
	{
		if (value == IntPtr.Zero)
		{
			return null;
		}

		var buffer = new byte[(int)CFStringGetLength(value) * 4 + 1];
		if (!CFStringGetCString(value, buffer, buffer.Length, CFStringEncodingUtf8))
		{
			return null;
		}

		return Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0));
	}

	public static bool IsCFString(IntPtr value) => CFGetTypeID(value) == CFStringGetTypeID();

	[DllImport(CoreFoundation)]
	public static extern void CFRelease(IntPtr handle);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool CFPreferencesAppSynchronize(IntPtr applicationId);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool CFPreferencesGetAppBooleanValue(IntPtr key, IntPtr applicationId, IntPtr keyExists);

	[DllImport(CoreFoundation)]
	public static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

	[DllImport(CoreFoundation)]
	public static extern IntPtr CFLocaleCreate(IntPtr allocator, IntPtr localeIdentifier);

	[DllImport(CoreFoundation)]
	public static extern IntPtr CFLocaleCopyCurrent();

	[DllImport(CoreFoundation)]
	public static extern IntPtr CFDateFormatterCreate(IntPtr allocator, IntPtr locale, nint dateStyle, nint timeStyle);

	[DllImport(CoreFoundation)]
	public static extern IntPtr CFDateFormatterGetFormat(IntPtr formatter);

	[DllImport(CoreFoundation)]
	private static extern nint CFStringGetLength(IntPtr value);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

	[DllImport(CoreFoundation)]
	private static extern nuint CFGetTypeID(IntPtr value);

	[DllImport(CoreFoundation)]
	private static extern nuint CFStringGetTypeID();

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
