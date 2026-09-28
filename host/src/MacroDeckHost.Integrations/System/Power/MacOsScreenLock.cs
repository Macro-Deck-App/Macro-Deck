using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Power;

[SupportedOSPlatform("macos")]
internal static class MacOsScreenLock
{
	// Private login.framework API, the only way to lock the session immediately since CGSession was removed.
	// Undocumented contract: it returns 0 once the SessionAgent has locked the session.
	private const string LoginFramework = "/System/Library/PrivateFrameworks/login.framework/Versions/Current/login";

	private static readonly Lazy<LockScreenImmediate?> _lockScreenImmediate = new(Resolve);

	public static bool IsAvailable => _lockScreenImmediate.Value is not null;

	public static int Lock()
		=> _lockScreenImmediate.Value?.Invoke()
			?? throw new PlatformNotSupportedException("SACLockScreenImmediate is not available.");

	private static LockScreenImmediate? Resolve()
		=> NativeLibrary.TryLoad(LoginFramework, out var handle)
			&& NativeLibrary.TryGetExport(handle, "SACLockScreenImmediate", out var address)
				? Marshal.GetDelegateForFunctionPointer<LockScreenImmediate>(address)
				: null;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int LockScreenImmediate();
}
