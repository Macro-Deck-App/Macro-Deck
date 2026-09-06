using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.Native;

internal static class MacOsAccessibility
{
	public const string ApplicationServices =
		"/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

	public const string CoreFoundation =
		"/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	private static int _startupPromptClaimed;

	public static bool TryClaimStartupPrompt() => Interlocked.Exchange(ref _startupPromptClaimed, 1) == 0;

	[SupportedOSPlatform("macos")]
	public static bool IsTrusted() => AXIsProcessTrusted();

	[SupportedOSPlatform("macos")]
	public static void RequestTrust()
	{
		// Build { kAXTrustedCheckOptionPrompt: true }. The key and boolean are exported CF constants
		// (no need to create or release them).
		var key = ReadGlobalRef(ApplicationServices, "kAXTrustedCheckOptionPrompt");
		var trueValue = ReadGlobalRef(CoreFoundation, "kCFBooleanTrue");
		if (key == IntPtr.Zero || trueValue == IntPtr.Zero)
		{
			_ = AXIsProcessTrustedWithOptions(IntPtr.Zero);
			return;
		}

		var options = CFDictionaryCreate(IntPtr.Zero, [key], [trueValue], 1, IntPtr.Zero, IntPtr.Zero);
		try
		{
			_ = AXIsProcessTrustedWithOptions(options);
		}
		finally
		{
			if (options != IntPtr.Zero)
			{
				CFRelease(options);
			}
		}
	}

	public static IntPtr ReadGlobalRef(string library, string symbol)
	{
		if (!NativeLibrary.TryLoad(library, out var handle) ||
			!NativeLibrary.TryGetExport(handle, symbol, out var address))
		{
			return IntPtr.Zero;
		}

		return Marshal.ReadIntPtr(address);
	}

	public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');

	[DllImport(ApplicationServices)]
	private static extern bool AXIsProcessTrusted();

	[DllImport(ApplicationServices)]
	private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr handle);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDictionaryCreate(
		IntPtr allocator,
		IntPtr[] keys,
		IntPtr[] values,
		nint numValues,
		IntPtr keyCallBacks,
		IntPtr valueCallBacks);
}
