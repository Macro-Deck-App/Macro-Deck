using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Native;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class Win32IntegrityWindowsTests
{
	private const uint LowLevel = 0x1000;
	private const uint HighLevel = 0x3000;
	private const uint TokenDuplicate = 0x0002;
	private const uint TokenQuery = 0x0008;
	private const uint TokenAssignPrimary = 0x0001;
	private const uint TokenAdjustDefault = 0x0080;
	private const uint MaximumAllowed = 0x02000000;
	private const int SecurityImpersonation = 2;
	private const int TokenPrimary = 1;
	private const int TokenIntegrityLevelClass = 25;
	private const uint SeGroupIntegrity = 0x00000020;
	private const uint CreateNoWindow = 0x08000000;

	[Test]
	public void The_host_reads_its_own_integrity_level()
	{
		var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
		var current = Win32Integrity.Current;

		Assert.That(current, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(Win32Integrity.Read((uint)Environment.ProcessId).Level, Is.EqualTo(current));
			Assert.That(current, elevated ? Is.GreaterThanOrEqualTo(HighLevel) : Is.LessThan(HighLevel));
		});
	}

	[Test]
	public void A_low_integrity_process_is_outranked_by_the_host()
	{
		var child = StartLowIntegrityChild();
		try
		{
			var read = Win32Integrity.Read(child.ProcessId);

			Assert.Multiple(() =>
			{
				Assert.That(read.Level, Is.EqualTo(LowLevel));
				Assert.That(Win32Integrity.Outranks(read, Win32Integrity.Current),
					Is.False,
					"a receiver below the sender is not blocked");
				Assert.That(Win32Integrity.Outranks(Win32Integrity.Read((uint)Environment.ProcessId), read.Level),
					Is.True,
					"a low sender is blocked by this process");
			});
		}
		finally
		{
			Stop(child);
		}
	}

	[TestCase(0u)]
	[TestCase(uint.MaxValue)]
	public void An_unknown_process_has_no_integrity_level(uint processId)
	{
		var read = Win32Integrity.Read(processId);

		Assert.Multiple(() =>
		{
			Assert.That(read.Level, Is.Null);
			Assert.That(read.Denied, Is.False);
			Assert.That(Win32Integrity.Outranks(read, Win32Integrity.Current), Is.False);
		});
	}

	[TestCase(0x2000u, true)]
	[TestCase(0x3000u, true)]
	[TestCase(0x4000u, false)]
	public void A_receiver_that_refuses_to_be_read_is_taken_as_outranking_a_sender_below_system(
		uint sender,
		bool outranks)
	{
		Assert.That(Win32Integrity.Outranks(new IntegrityRead(null, true), sender), Is.EqualTo(outranks));
	}

	[Test]
	public void A_receiver_of_unknown_level_that_does_not_refuse_is_not_taken_as_outranking()
	{
		Assert.That(Win32Integrity.Outranks(new IntegrityRead(null, false), LowLevel), Is.False);
	}

	private static ProcessInformation StartLowIntegrityChild()
	{
		var commandLine = $"\"{Path.Combine(Environment.SystemDirectory, "PING.EXE")}\" -n 60 127.0.0.1\0"
			.ToCharArray();
		using var current = Process.GetCurrentProcess();
		if (!OpenProcessToken(current.Handle,
			TokenDuplicate | TokenQuery | TokenAssignPrimary | TokenAdjustDefault,
			out var token))
		{
			Assert.Inconclusive($"Cannot open the process token: {Marshal.GetLastWin32Error()}");
		}

		try
		{
			if (!DuplicateTokenEx(token,
				MaximumAllowed,
				IntPtr.Zero,
				SecurityImpersonation,
				TokenPrimary,
				out var lowToken))
			{
				Assert.Inconclusive($"Cannot duplicate the process token: {Marshal.GetLastWin32Error()}");
			}

			try
			{
				LowerIntegrity(lowToken);
				var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
				if (!CreateProcessAsUserW(lowToken,
					null,
					commandLine,
					IntPtr.Zero,
					IntPtr.Zero,
					false,
					CreateNoWindow,
					IntPtr.Zero,
					null,
					ref startup,
					out var info))
				{
					Assert.Inconclusive($"CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");
				}

				return info;
			}
			finally
			{
				_ = CloseHandle(lowToken);
			}
		}
		finally
		{
			_ = CloseHandle(token);
		}
	}

	private static void LowerIntegrity(IntPtr token)
	{
		if (!ConvertStringSidToSidW("S-1-16-4096", out var sid))
		{
			Assert.Inconclusive($"Cannot build the low integrity SID: {Marshal.GetLastWin32Error()}");
		}

		try
		{
			var label = new TokenMandatoryLabel { Sid = sid, Attributes = SeGroupIntegrity };
			var size = Marshal.SizeOf<TokenMandatoryLabel>() + GetLengthSid(sid);
			if (!SetTokenInformation(token, TokenIntegrityLevelClass, ref label, size))
			{
				Assert.Inconclusive($"Cannot lower the integrity level: {Marshal.GetLastWin32Error()}");
			}
		}
		finally
		{
			_ = LocalFree(sid);
		}
	}

	private static void Stop(ProcessInformation child)
	{
		_ = TerminateProcess(child.Process, 0);
		_ = WaitForSingleObject(child.Process, 5000);
		_ = CloseHandle(child.Thread);
		_ = CloseHandle(child.Process);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TokenMandatoryLabel
	{
		public IntPtr Sid;
		public uint Attributes;
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
		public IntPtr lpReserved2;
		public IntPtr hStdInput;
		public IntPtr hStdOutput;
		public IntPtr hStdError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInformation
	{
		public IntPtr Process;
		public IntPtr Thread;
		public uint ProcessId;
		public uint ThreadId;
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DuplicateTokenEx(
		IntPtr token,
		uint access,
		IntPtr attributes,
		int impersonationLevel,
		int tokenType,
		out IntPtr newToken);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ConvertStringSidToSidW(string sid, out IntPtr sidPointer);

	[DllImport("advapi32.dll")]
	private static extern int GetLengthSid(IntPtr sid);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetTokenInformation(
		IntPtr token,
		int informationClass,
		ref TokenMandatoryLabel information,
		int length);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateProcessAsUserW(
		IntPtr token,
		string? applicationName,
		[In] [Out] char[] commandLine,
		IntPtr processAttributes,
		IntPtr threadAttributes,
		[MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
		uint creationFlags,
		IntPtr environment,
		string? currentDirectory,
		ref StartupInfo startupInfo,
		out ProcessInformation processInformation);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TerminateProcess(IntPtr process, uint exitCode);

	[DllImport("kernel32.dll")]
	private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr memory);
}
