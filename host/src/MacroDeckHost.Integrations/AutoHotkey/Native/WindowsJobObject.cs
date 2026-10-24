using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.AutoHotkey.Native;

[SupportedOSPlatform("windows")]
internal sealed class WindowsJobObject : IDisposable
{
	private const int JobObjectExtendedLimitInformationClass = 9;
	private const uint JobObjectLimitSilentBreakawayOk = 0x00001000;
	private const uint JobObjectLimitKillOnJobClose = 0x00002000;

	private readonly Lock _gate = new();
	private IntPtr _handle;

	private WindowsJobObject(IntPtr handle)
	{
		_handle = handle;
	}

	// Kill on close makes the kernel end every assigned AutoHotkey process when the host exits or crashes.
	// Silent breakaway keeps programs a script launches out of the job, so they outlive the script.
	public static WindowsJobObject Create()
	{
		var handle = CreateJobObject(IntPtr.Zero, null);
		if (handle == IntPtr.Zero)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}

		var info = new JobObjectExtendedLimitInformation
		{
			BasicLimitInformation = new JobObjectBasicLimitInformation
			{
				LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitSilentBreakawayOk
			}
		};

		var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
		var buffer = Marshal.AllocHGlobal(length);
		try
		{
			Marshal.StructureToPtr(info, buffer, false);
			if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, buffer, (uint)length))
			{
				var error = Marshal.GetLastWin32Error();
				CloseHandle(handle);
				throw new Win32Exception(error);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}

		return new WindowsJobObject(handle);
	}

	public bool Assign(Process process)
	{
		lock (_gate)
		{
			return _handle != IntPtr.Zero && AssignProcessToJobObject(_handle, process.Handle);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_handle == IntPtr.Zero)
			{
				return;
			}

			CloseHandle(_handle);
			_handle = IntPtr.Zero;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateJobObjectW")]
	private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetInformationJobObject(
		IntPtr job,
		int informationClass,
		IntPtr information,
		uint informationLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}
