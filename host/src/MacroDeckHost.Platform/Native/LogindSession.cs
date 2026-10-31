using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("linux")]
internal static class LogindSession
{
	private const string LibSystemd = "libsystemd.so.0";

	private static readonly byte[] _destination = "org.freedesktop.login1\0"u8.ToArray();

	// auto is the caller's session, else the user's display session: apps started under user@.service
	// or through XDG autostart sit outside the session cgroup, where self fails.
	private static readonly byte[] _sessionPath = "/org/freedesktop/login1/session/auto\0"u8.ToArray();

	private static readonly byte[] _sessionInterface = "org.freedesktop.login1.Session\0"u8.ToArray();

	private static readonly byte[] _lockedHint = "LockedHint\0"u8.ToArray();

	private const byte BooleanType = (byte)'b';

	private const ulong CallTimeoutMicroseconds = 1_000_000;

	public static bool? ReadLockedHint()
	{
		try
		{
			if (sd_bus_open_system(out var bus) < 0)
			{
				return null;
			}

			try
			{
				_ = sd_bus_set_method_call_timeout(bus, CallTimeoutMicroseconds);
				var result = sd_bus_get_property_trivial(bus,
					_destination,
					_sessionPath,
					_sessionInterface,
					_lockedHint,
					IntPtr.Zero,
					BooleanType,
					out var locked);

				return result < 0 ? null : locked != 0;
			}
			finally
			{
				_ = sd_bus_unref(bus);
			}
		}
		catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
		{
			return null;
		}
	}

	[DllImport(LibSystemd)]
	private static extern int sd_bus_open_system(out IntPtr bus);

	[DllImport(LibSystemd)]
	private static extern int sd_bus_set_method_call_timeout(IntPtr bus, ulong microseconds);

	// sd-bus writes a C int for a boolean property, so the out target has to be four bytes wide.
	[DllImport(LibSystemd)]
	private static extern int sd_bus_get_property_trivial(
		IntPtr bus,
		byte[] destination,
		byte[] path,
		byte[] @interface,
		byte[] member,
		IntPtr error,
		byte type,
		out int value);

	[DllImport(LibSystemd)]
	private static extern IntPtr sd_bus_unref(IntPtr bus);
}
