using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using MacroDeckHost.Integrations.Discord.Protocol;
using Microsoft.Win32.SafeHandles;

namespace MacroDeckHost.Integrations.Discord.Native;

[SupportedOSPlatform("windows")]
internal static class WindowsPipeServerProbe
{
	private const uint FileReadAttributes = 0x80;
	private const uint ReadControl = 0x20000;
	private const uint FileShareReadWrite = 0x3;
	private const uint OpenExisting = 3;
	private const int ErrorPipeBusy = 231;
	private const uint PipeBusyWaitMilliseconds = 250;
	private const uint ProcessQueryLimitedInformation = 0x1000;
	private const uint TokenQuery = 0x8;
	private const int TokenUserClass = 1;
	private const int TokenElevationClass = 20;
	private const int SeKernelObject = 6;
	private const uint OwnerSecurityInformation = 0x1;

	// A pipe that denies read/write still lets Everyone read attributes, which is enough to ask
	// for the server process without being able to talk to it.
	public static DiscordIpcProcess? FindServer(string pipeName)
	{
		var path = $@"\\.\pipe\{pipeName}";
		using var handle = Open(path, FileReadAttributes | ReadControl) ?? Open(path, FileReadAttributes);
		if (handle is null || !GetNamedPipeServerProcessId(handle, out var processId))
		{
			return null;
		}

		int? sessionId = GetNamedPipeServerSessionId(handle, out var session) ? (int)session : null;
		var server = Describe((int)processId, sessionId);

		// An elevated server may close its token to us, but what it creates is owned by Administrators
		// instead of the user, which still tells elevation apart.
		return server.Elevated is null && IsOwnedByAdministrators(handle) ? server with { Elevated = true } : server;
	}

	public static DiscordIpcProcess Current()
	{
		using var process = Process.GetCurrentProcess();
		return Describe(process.Id, process.SessionId);
	}

	private static SafeFileHandle? Open(string path, uint access)
	{
		for (var attempt = 0; attempt < 2; attempt++)
		{
			var handle = CreateFileW(path,
				access,
				FileShareReadWrite,
				IntPtr.Zero,
				OpenExisting,
				0,
				IntPtr.Zero);
			if (!handle.IsInvalid)
			{
				return handle;
			}

			var error = Marshal.GetLastWin32Error();
			handle.Dispose();
			if (error != ErrorPipeBusy || !WaitNamedPipeW(path, PipeBusyWaitMilliseconds))
			{
				return null;
			}
		}

		return null;
	}

	private static bool IsOwnedByAdministrators(SafeFileHandle handle)
	{
		var result = GetSecurityInfo(handle,
			SeKernelObject,
			OwnerSecurityInformation,
			out var owner,
			IntPtr.Zero,
			IntPtr.Zero,
			IntPtr.Zero,
			out var descriptor);
		if (result != 0)
		{
			return false;
		}

		try
		{
			return new SecurityIdentifier(owner).IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
		}
		catch (ArgumentException)
		{
			return false;
		}
		finally
		{
			LocalFree(descriptor);
		}
	}

	private static DiscordIpcProcess Describe(int processId, int? sessionId)
	{
		var (elevated, userSid) = ReadToken(processId);
		return new DiscordIpcProcess(processId, ProcessName(processId), sessionId, elevated, userSid);
	}

	private static string? ProcessName(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			return process.ProcessName;
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
		{
			return null;
		}
	}

	private static (bool? Elevated, string? UserSid) ReadToken(int processId)
	{
		var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
		if (process == IntPtr.Zero)
		{
			return (null, null);
		}

		try
		{
			if (!OpenProcessToken(process, TokenQuery, out var token))
			{
				return (null, null);
			}

			try
			{
				bool? elevated = GetTokenInformation(token, TokenElevationClass, out var elevation, sizeof(int), out _)
					? elevation != 0
					: null;
				return (elevated, ReadUserSid(token));
			}
			finally
			{
				CloseHandle(token);
			}
		}
		finally
		{
			CloseHandle(process);
		}
	}

	private static string? ReadUserSid(IntPtr token)
	{
		GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out var length);
		if (length <= 0)
		{
			return null;
		}

		var buffer = Marshal.AllocHGlobal(length);
		try
		{
			if (!GetTokenInformation(token, TokenUserClass, buffer, length, out _))
			{
				return null;
			}

			return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
		}
		catch (ArgumentException)
		{
			return null;
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern SafeFileHandle CreateFileW(
		string fileName,
		uint desiredAccess,
		uint shareMode,
		IntPtr securityAttributes,
		uint creationDisposition,
		uint flagsAndAttributes,
		IntPtr templateFile);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool WaitNamedPipeW(string name, uint timeout);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetNamedPipeServerProcessId(SafeFileHandle pipe, out uint processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetNamedPipeServerSessionId(SafeFileHandle pipe, out uint sessionId);

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
		out int information,
		int length,
		out int returnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(
		IntPtr token,
		int informationClass,
		IntPtr information,
		int length,
		out int returnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern uint GetSecurityInfo(
		SafeFileHandle handle,
		int objectType,
		uint securityInfo,
		out IntPtr owner,
		IntPtr group,
		IntPtr dacl,
		IntPtr sacl,
		out IntPtr securityDescriptor);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr memory);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}
