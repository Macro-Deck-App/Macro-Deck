using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Infrastructure.Plugins.Jobs.Native;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Jobs;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPluginProcessJob : IPluginProcessJob
{
	// The handle is owned outright and never wrapped in a finalizable holder: with
	// KILL_ON_JOB_CLOSE a GC time close would terminate a healthy plugin tree at an arbitrary moment.
	private readonly ILogger _logger;

	private IntPtr _handle;

	private WindowsPluginProcessJob(IntPtr handle, ILogger logger)
	{
		_handle = handle;
		_logger = logger;
	}

	public static WindowsPluginProcessJob? TryCreate(ILogger logger)
	{
		var handle = WindowsJobObjectInterop.CreateJobObject(IntPtr.Zero, name: null);
		if (handle == IntPtr.Zero)
		{
			return null;
		}

		if (!TrySetKillOnClose(handle))
		{
			WindowsJobObjectInterop.CloseHandle(handle);
			return null;
		}

		return new WindowsPluginProcessJob(handle, logger);
	}

	public bool IsActive => _handle != IntPtr.Zero;

	public bool TryAssign(int processId)
	{
		if (_handle == IntPtr.Zero || processId <= 0)
		{
			return false;
		}

		var process = WindowsJobObjectInterop.OpenProcess(
			WindowsJobObjectInterop.ProcessSetQuota | WindowsJobObjectInterop.ProcessTerminate,
			inheritHandle: false,
			processId);
		if (process == IntPtr.Zero)
		{
			PluginInfrastructureLog.JobOpenProcessFailed(_logger, processId, Marshal.GetLastWin32Error());
			return false;
		}

		try
		{
			return WindowsJobObjectInterop.AssignProcessToJobObject(_handle, process);
		}
		finally
		{
			WindowsJobObjectInterop.CloseHandle(process);
		}
	}

	public bool TryTerminate()
		=> _handle != IntPtr.Zero && WindowsJobObjectInterop.TerminateJobObject(_handle, exitCode: 1);

	public void Dispose()
	{
		var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
		if (handle != IntPtr.Zero)
		{
			WindowsJobObjectInterop.CloseHandle(handle);
		}
	}

	private static bool TrySetKillOnClose(IntPtr handle)
	{
		var information = new WindowsJobObjectInterop.JobObjectExtendedLimitInformation
		{
			BasicLimitInformation = new WindowsJobObjectInterop.JobObjectBasicLimitInformation
			{
				LimitFlags = WindowsJobObjectInterop.JobObjectLimitKillOnJobClose
			}
		};

		var length = Marshal.SizeOf<WindowsJobObjectInterop.JobObjectExtendedLimitInformation>();
		var buffer = Marshal.AllocHGlobal(length);
		try
		{
			Marshal.StructureToPtr(information, buffer, fDeleteOld: false);
			return WindowsJobObjectInterop.SetInformationJobObject(handle,
				WindowsJobObjectInterop.JobObjectExtendedLimitInformationClass,
				buffer,
				(uint)length);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}
}
