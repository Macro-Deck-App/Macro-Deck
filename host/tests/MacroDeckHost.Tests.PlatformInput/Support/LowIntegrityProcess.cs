using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Tests.Support;

[SupportedOSPlatform("windows")]
internal static class LowIntegrityProcess
{
	private const uint TokenDuplicate = 0x0002;
	private const uint TokenQuery = 0x0008;
	private const uint TokenAssignPrimary = 0x0001;
	private const uint TokenAdjustDefault = 0x0080;
	private const uint MaximumAllowed = 0x02000000;
	private const int SecurityImpersonation = 2;
	private const int TokenPrimary = 1;
	private const int TokenIntegrityLevel = 25;
	private const uint SeGroupIntegrity = 0x00000020;
	private const uint StartfUseStdHandles = 0x00000100;
	private const uint CreateNoWindow = 0x08000000;
	private const uint HandleFlagInherit = 0x00000001;

	// UIPI only blocks a sender below the target's integrity level, so the sender runs at low integrity.
	public static ProbeProcess StartProbe(string name, params string[] arguments)
	{
		var commandLine = string.Join(' ', arguments.Prepend(ProbeLocator.Executable(name)).Select(a => $"\"{a}\""))
			.Append('\0')
			.ToArray();

		if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
				TokenDuplicate | TokenQuery | TokenAssignPrimary | TokenAdjustDefault,
				out var token)
			|| !DuplicateTokenEx(token,
				MaximumAllowed,
				IntPtr.Zero,
				SecurityImpersonation,
				TokenPrimary,
				out var lowToken))
		{
			throw new InvalidOperationException($"Cannot duplicate the process token: {Marshal.GetLastWin32Error()}");
		}

		try
		{
			LowerIntegrity(lowToken);
			var (readOut, writeOut) = Pipe();
			var startup = new StartupInfo
			{
				cb = Marshal.SizeOf<StartupInfo>(),
				dwFlags = StartfUseStdHandles,
				hStdOutput = writeOut,
				hStdError = writeOut
			};
			if (!CreateProcessAsUserW(lowToken,
				null,
				commandLine,
				IntPtr.Zero,
				IntPtr.Zero,
				true,
				CreateNoWindow,
				IntPtr.Zero,
				Path.GetDirectoryName(ProbeLocator.Executable(name)),
				ref startup,
				out var info))
			{
				throw new InvalidOperationException($"CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");
			}

			_ = CloseHandle(writeOut);
			_ = CloseHandle(info.hThread);
			_ = CloseHandle(info.hProcess);
			return ProbeProcess.Attach(name, info.dwProcessId, readOut);
		}
		finally
		{
			_ = CloseHandle(lowToken);
			_ = CloseHandle(token);
		}
	}

	private static void LowerIntegrity(nint token)
	{
		if (!ConvertStringSidToSidW("S-1-16-4096", out var sid))
		{
			throw new InvalidOperationException($"Cannot build the low integrity SID: {Marshal.GetLastWin32Error()}");
		}

		try
		{
			var label = new TokenMandatoryLabel { Sid = sid, Attributes = SeGroupIntegrity };
			var size = Marshal.SizeOf<TokenMandatoryLabel>() + GetLengthSid(sid);
			if (!SetTokenInformation(token, TokenIntegrityLevel, ref label, size))
			{
				throw new InvalidOperationException($"Cannot lower the integrity level: {Marshal.GetLastWin32Error()}");
			}
		}
		finally
		{
			_ = LocalFree(sid);
		}
	}

	private static (nint Read, nint Write) Pipe()
	{
		var attributes = new SecurityAttributes
			{ nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = true };
		if (!CreatePipe(out var read, out var write, ref attributes, 0))
		{
			throw new InvalidOperationException($"CreatePipe failed: {Marshal.GetLastWin32Error()}");
		}

		_ = SetHandleInformation(read, HandleFlagInherit, 0);
		return (read, write);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TokenMandatoryLabel
	{
		public nint Sid;
		public uint Attributes;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SecurityAttributes
	{
		public int nLength;
		public nint lpSecurityDescriptor;
		public bool bInheritHandle;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo
	{
		public int cb;
		public string? lpReserved;
		public string? lpDesktop;
		public string? lpTitle;
		public int dwX;
		public int dwY;
		public int dwXSize;
		public int dwYSize;
		public int dwXCountChars;
		public int dwYCountChars;
		public int dwFillAttribute;
		public uint dwFlags;
		public short wShowWindow;
		public short cbReserved2;
		public nint lpReserved2;
		public nint hStdInput;
		public nint hStdOutput;
		public nint hStdError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInformation
	{
		public nint hProcess;
		public nint hThread;
		public int dwProcessId;
		public int dwThreadId;
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool DuplicateTokenEx(
		nint token,
		uint access,
		nint attributes,
		int impersonationLevel,
		int tokenType,
		out nint newToken);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool ConvertStringSidToSidW(string sid, out nint sidPointer);

	[DllImport("advapi32.dll")]
	private static extern int GetLengthSid(nint sid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool SetTokenInformation(
		nint token,
		int informationClass,
		ref TokenMandatoryLabel information,
		int length);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateProcessAsUserW(
		nint token,
		string? applicationName,
		[In] [Out] char[] commandLine,
		nint processAttributes,
		nint threadAttributes,
		bool inheritHandles,
		uint creationFlags,
		nint environment,
		string? currentDirectory,
		ref StartupInfo startupInfo,
		out ProcessInformation processInformation);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CreatePipe(out nint read, out nint write, ref SecurityAttributes attributes, int size);

	[DllImport("kernel32.dll")]
	private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint memory);
}
