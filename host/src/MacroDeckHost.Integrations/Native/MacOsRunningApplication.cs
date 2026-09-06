using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Native;

internal readonly record struct MacOsRunningApplicationInfo(int ProcessId, string? BundleId, string? ExecutablePath);

// Shared by MacOsFocusedWindowReader (polling) and MacOsFocusedApplicationWatcher (native NSWorkspace
// notifications) so the two focus paths always read the frontmost application the same way.
[SupportedOSPlatform("macos")]
internal static class MacOsRunningApplication
{
	private const string LibObjC = "/usr/lib/libobjc.dylib";
	private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

	public static MacOsRunningApplicationInfo? TryReadFrontmost()
	{
		// bundleIdentifier, executableURL and -[NSURL path] below all return autoreleased values. On the
		// main thread, CFRunLoop wraps each callout in its own pool, but the seed read in
		// MacOsFocusedApplicationWatcher.WatchAsync runs on a thread-pool thread with no run loop at all,
		// so an explicit pool is required here rather than relying on that as an implementation detail.
		var pool = objc_autoreleasePoolPush();
		try
		{
			if (!NativeLibrary.TryLoad(AppKit, out _))
			{
				return null;
			}

			var workspaceClass = objc_getClass(Utf8("NSWorkspace"));
			if (workspaceClass == IntPtr.Zero)
			{
				return null;
			}

			var workspace = objc_msgSend_noArgs(workspaceClass, sel_registerName(Utf8("sharedWorkspace")));
			if (workspace == IntPtr.Zero)
			{
				return null;
			}

			var app = objc_msgSend_noArgs(workspace, sel_registerName(Utf8("frontmostApplication")));
			return TryRead(app);
		}
		catch
		{
			return null;
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}

	public static MacOsRunningApplicationInfo? TryRead(IntPtr app)
	{
		if (app == IntPtr.Zero)
		{
			return null;
		}

		var pool = objc_autoreleasePoolPush();
		try
		{
			var pid = (int)objc_msgSend_intRet(app, sel_registerName(Utf8("processIdentifier")));
			var bundleId = ReadNSString(app, "bundleIdentifier");
			var url = objc_msgSend_noArgs(app, sel_registerName(Utf8("executableURL")));
			var path = url == IntPtr.Zero ? null : ReadNSString(url, "path");
			return new MacOsRunningApplicationInfo(pid, bundleId, path);
		}
		catch
		{
			return null;
		}
		finally
		{
			objc_autoreleasePoolPop(pool);
		}
	}

	private static string? ReadNSString(IntPtr receiver, string selectorName)
	{
		var value = objc_msgSend_noArgs(receiver, sel_registerName(Utf8(selectorName)));
		if (value == IntPtr.Zero)
		{
			return null;
		}

		var utf8 = objc_msgSend_noArgs(value, sel_registerName(Utf8("UTF8String")));
		return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
	}

	private static byte[] Utf8(string value) => MacOsAccessibility.Utf8(value);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_getClass(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr sel_registerName(byte[] name);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_noArgs(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern nint objc_msgSend_intRet(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC)]
	private static extern IntPtr objc_autoreleasePoolPush();

	[DllImport(LibObjC)]
	private static extern void objc_autoreleasePoolPop(IntPtr handle);
}
