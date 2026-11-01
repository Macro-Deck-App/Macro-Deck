using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("macos")]
internal static class MacOsBundles
{
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const string LibSystem = "/usr/lib/libSystem.dylib";

	private const int PathBufferSize = 4096;

	public static bool IsAppBundle(string path)
	{
		return path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase);
	}

	// The running process reports its canonical path, so the bundle executable is canonicalized too.
	public static string? TryGetExecutablePath(string bundlePath)
	{
		var utf8 = MacOsAccessibility.Utf8(bundlePath.TrimEnd('/'));
		var url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, utf8, utf8.Length - 1, true);
		if (url == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var bundle = CFBundleCreate(IntPtr.Zero, url);
			if (bundle == IntPtr.Zero)
			{
				return null;
			}

			try
			{
				var executable = CFBundleCopyExecutableURL(bundle);
				if (executable == IntPtr.Zero)
				{
					return null;
				}

				try
				{
					return ReadCanonicalPath(executable);
				}
				finally
				{
					MacOsCoreFoundation.CFRelease(executable);
				}
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(bundle);
			}
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(url);
		}
	}

	private static string? ReadCanonicalPath(IntPtr url)
	{
		var buffer = new byte[PathBufferSize];
		if (!CFURLGetFileSystemRepresentation(url, true, buffer, buffer.Length))
		{
			return null;
		}

		var resolved = new byte[PathBufferSize];
		return realpath(buffer, resolved) == IntPtr.Zero
			? null
			: Encoding.UTF8.GetString(resolved, 0, Array.IndexOf(resolved, (byte)0));
	}

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
		IntPtr allocator,
		byte[] buffer,
		nint length,
		[MarshalAs(UnmanagedType.I1)] bool isDirectory);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFURLGetFileSystemRepresentation(
		IntPtr url,
		[MarshalAs(UnmanagedType.I1)] bool resolveAgainstBase,
		byte[] buffer,
		nint maxLength);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFBundleCreate(IntPtr allocator, IntPtr bundleUrl);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFBundleCopyExecutableURL(IntPtr bundle);

	[DllImport(LibSystem)]
	private static extern IntPtr realpath(byte[] path, byte[] resolved);
}
