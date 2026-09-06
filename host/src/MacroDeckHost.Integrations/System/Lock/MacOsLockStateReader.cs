using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Lock;

[SupportedOSPlatform("macos")]
public sealed class MacOsLockStateReader : ILockStateReader
{
	private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

	private const string ScreenIsLockedKey = "CGSSessionScreenIsLocked";

	public bool IsSupported => OperatingSystem.IsMacOS();

	public string? UnsupportedReason => null;

	public bool? IsLocked()
	{
		try
		{
			// Only succeeds for the GUI session owner, which is how the host runs; a failed read
			// (headless session, sandboxed process, ...) returns null rather than a guess.
			var session = CGSessionCopyCurrentDictionary();
			if (session == IntPtr.Zero)
			{
				return null;
			}

			try
			{
				var key = MacOsCoreFoundation.CreateCFString(ScreenIsLockedKey);
				if (key == IntPtr.Zero)
				{
					return null;
				}

				try
				{
					// The key is only present while the screen is locked. A session dictionary that
					// answers without it means unlocked - reporting "unknown" here would leave the
					// last locked value standing forever and block every action until a restart.
					return MacOsCoreFoundation.TryReadCFBool(session, key, out var locked) && locked;
				}
				finally
				{
					MacOsCoreFoundation.CFRelease(key);
				}
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(session);
			}
		}
		catch
		{
			return null;
		}
	}

	[DllImport(CoreGraphics)]
	private static extern IntPtr CGSessionCopyCurrentDictionary();
}
