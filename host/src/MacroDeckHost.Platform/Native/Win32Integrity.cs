using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("windows")]
internal static class Win32Integrity
{
	public const uint SystemLevel = 0x4000;

	private const uint ProcessQueryLimitedInformation = 0x1000;
	private const uint TokenQuery = 0x0008;
	private const int TokenIntegrityLevelClass = 25;
	private const int ErrorAccessDenied = 5;

	public static uint? Current => Read((uint)Environment.ProcessId).Level;

	public static IntegrityRead Read(uint processId)
	{
		if (processId == 0)
		{
			return default;
		}

		var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
		if (process == IntPtr.Zero)
		{
			return new IntegrityRead(null, Marshal.GetLastWin32Error() == ErrorAccessDenied);
		}

		try
		{
			if (!OpenProcessToken(process, TokenQuery, out var token))
			{
				return new IntegrityRead(null, Marshal.GetLastWin32Error() == ErrorAccessDenied);
			}

			try
			{
				return new IntegrityRead(ReadLevel(token), false);
			}
			finally
			{
				_ = CloseHandle(token);
			}
		}
		finally
		{
			_ = CloseHandle(process);
		}
	}

	// UIPI drops input sent to a window of a higher integrity level. An elevated process or its token is
	// not readable by a non-elevated one, so a denied query is taken as a likely elevated receiver.
	public static bool Outranks(IntegrityRead receiver, uint? sender)
	{
		if (sender is not { } senderLevel)
		{
			return false;
		}

		if (receiver.Level is { } receiverLevel)
		{
			return receiverLevel > senderLevel;
		}

		return receiver.Denied && senderLevel < SystemLevel;
	}

	private static uint? ReadLevel(IntPtr token)
	{
		_ = GetTokenInformation(token, TokenIntegrityLevelClass, IntPtr.Zero, 0, out var length);
		if (length <= 0)
		{
			return null;
		}

		var buffer = Marshal.AllocHGlobal(length);
		try
		{
			if (!GetTokenInformation(token, TokenIntegrityLevelClass, buffer, length, out _))
			{
				return null;
			}

			var sid = Marshal.ReadIntPtr(buffer);
			var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
			return count == 0 ? null : (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(
		uint desiredAccess,
		[MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
		uint processId);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(
		IntPtr token,
		int informationClass,
		IntPtr information,
		int length,
		out int returnLength);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}

internal readonly record struct IntegrityRead(uint? Level, bool Denied);
