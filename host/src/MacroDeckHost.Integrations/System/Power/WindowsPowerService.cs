using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.System.Power;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerService : IPowerService
{
	private const string SeShutdownName = "SeShutdownPrivilege";
	private const uint TokenAdjustPrivileges = 0x0020;
	private const uint TokenQuery = 0x0008;
	private const uint SePrivilegeEnabled = 0x00000002;
	private const int ErrorNotAllAssigned = 1300;

	private const uint ShutdownRestart = 0x00000004;
	private const uint ShutdownPowerOff = 0x00000008;
	private const uint ShutdownForceOthers = 0x00000001;

	private const uint ShutdownReason = 0x00040000 | 0x80000000;

	private const int SystemPowerCapabilitiesLevel = 4;

	private static readonly ILogger _logger =
		IntegrationLog.For<IPowerService>(SystemIntegration.IntegrationId);

	public bool IsSupported => true;

	public bool Supports(PowerOperation operation)
		=> operation switch
		{
			PowerOperation.Hibernate => IsHibernationAvailable(),
			_ => true
		};

	public async Task<PowerResult> ExecuteAsync(
		PowerOperation operation,
		bool force,
		CancellationToken cancellationToken = default)
	{
		// Every operation except Lock needs SE_SHUTDOWN_NAME, not just shutdown/restart: SetSuspendState
		// also requires it. LockWorkStation needs no privilege at all, so it must not be gated on one
		// that a locked-down policy may have revoked - locking is the one operation that has to keep
		// working there.
		if (operation != PowerOperation.Lock && !TryEnableShutdownPrivilege(out var privilegeError))
		{
			_logger.Warning("Could not enable SeShutdownPrivilege: {Error}", privilegeError);
			return PowerResult.Failed(AppStrings.Integrations.System.Errors.Power.WindowsPrivilegeFailed());
		}

		return operation switch
		{
			PowerOperation.Lock => ExecuteLock(),
			PowerOperation.Sleep => ExecuteSuspend(hibernate: false),
			PowerOperation.Hibernate => await ExecuteHibernateAsync(),
			PowerOperation.Restart => await ExecuteShutdownAsync(ShutdownRestart, force),
			PowerOperation.ShutDown => await ExecuteShutdownAsync(ShutdownPowerOff, force),
			_ => PowerResult.Failed(AppStrings.Integrations.System.Errors.Power.UnsupportedOperation())
		};
	}

	private static PowerResult ExecuteLock()
	{
		if (LockWorkStation())
		{
			return PowerResult.Succeeded();
		}

		var error = Marshal.GetLastWin32Error();
		_logger.Warning("LockWorkStation failed with Win32 error {Error}", error);
		return PowerResult.Failed(AppStrings.Integrations.System.Errors.Power.WindowsLockFailed());
	}

	private static Task<PowerResult> ExecuteHibernateAsync()
	{
		if (!IsHibernationAvailable())
		{
			return Task.FromResult(PowerResult.Failed(
				AppStrings.Integrations.System.Errors.Power.WindowsHibernationDisabled(),
				ActionErrorCodes.Unavailable));
		}

		return Task.FromResult(ExecuteSuspend(hibernate: true));
	}

	private static PowerResult ExecuteSuspend(bool hibernate)
	{
		if (SetSuspendState(hibernate, forceCritical: false, disableWakeEvent: false))
		{
			return PowerResult.Succeeded();
		}

		var error = Marshal.GetLastWin32Error();
		_logger.Warning("SetSuspendState({Hibernate}) failed with Win32 error {Error}", hibernate, error);
		return PowerResult.Failed(hibernate
			? AppStrings.Integrations.System.Errors.Power.HibernateFailed()
			: AppStrings.Integrations.System.Errors.Power.SleepFailed());
	}

	private static Task<PowerResult> ExecuteShutdownAsync(uint operationFlag, bool force)
	{
		var flags = operationFlag | (force ? ShutdownForceOthers : 0);
		var result = InitiateShutdownW(null, null, 0, flags, ShutdownReason);
		if (result == 0)
		{
			return Task.FromResult(PowerResult.Succeeded());
		}

		_logger.Warning("InitiateShutdownW failed with Win32 error {Error}", result);
		return Task.FromResult(PowerResult.Failed(AppStrings.Integrations.System.Errors.Power.WindowsRequestRefused()));
	}

	private static bool IsHibernationAvailable()
	{
		var status = CallNtPowerInformation(SystemPowerCapabilitiesLevel,
			IntPtr.Zero,
			0,
			out var capabilities,
			(uint)Marshal.SizeOf<SystemPowerCapabilities>());

		return status == 0 && capabilities.SystemS4 != 0 && capabilities.HiberFilePresent != 0;
	}

	private static bool TryEnableShutdownPrivilege(out string? error)
	{
		error = null;
		if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
		{
			error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
			return false;
		}

		try
		{
			if (!LookupPrivilegeValueW(null, SeShutdownName, out var luid))
			{
				error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
				return false;
			}

			var privileges = new TokenPrivileges
			{
				PrivilegeCount = 1,
				Luid = luid,
				Attributes = SePrivilegeEnabled
			};

			var adjusted = AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);

			var lastError = Marshal.GetLastWin32Error();
			if (!adjusted || lastError == ErrorNotAllAssigned)
			{
				error = new Win32Exception(lastError).Message;
				return false;
			}

			return true;
		}
		finally
		{
			CloseHandle(token);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Luid
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TokenPrivileges
	{
		public uint PrivilegeCount;
		public Luid Luid;
		public uint Attributes;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SystemPowerCapabilities
	{
		public byte PowerButtonPresent;
		public byte SleepButtonPresent;
		public byte LidPresent;
		public byte SystemS1;
		public byte SystemS2;
		public byte SystemS3;
		public byte SystemS4;
		public byte SystemS5;
		public byte HiberFilePresent;
		public byte FullWake;
		public byte VideoDimPresent;
		public byte ApmPresent;
		public byte UpsPresent;
		public byte ThermalControl;
		public byte ProcessorThrottle;
		public byte ProcessorMinThrottle;
		public byte ProcessorMaxThrottle;
		public byte FastSystemS4;
		public byte Spare2A;
		public byte Spare2B;
		public byte Spare2C;
		public byte DiskSpinDown;
		public byte Spare3A;
		public byte Spare3B;
		public byte Spare3C;
		public byte Spare3D;
		public byte Spare3E;
		public byte Spare3F;
		public byte Spare3G;
		public byte Spare3H;
		public byte SystemBatteriesPresent;
		public byte BatteriesAreShortTerm;
		public BatteryReportingScale BatteryScale0;
		public BatteryReportingScale BatteryScale1;
		public BatteryReportingScale BatteryScale2;
		public int AcOnLineWake;
		public int SoftLidWake;
		public int RtcWake;
		public int MinDeviceWakeState;
		public int DefaultLowLatencyWake;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BatteryReportingScale
	{
		public uint Granularity;
		public uint Capacity;
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool LockWorkStation();

	[DllImport("powrprof.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool SetSuspendState(
		[MarshalAs(UnmanagedType.U1)] bool hibernate,
		[MarshalAs(UnmanagedType.U1)] bool forceCritical,
		[MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

	[DllImport("powrprof.dll")]
	private static extern int CallNtPowerInformation(
		int informationLevel,
		IntPtr inputBuffer,
		uint inputBufferLength,
		out SystemPowerCapabilities outputBuffer,
		uint outputBufferLength);

	// Chosen over the legacy ExitWindowsEx: InitiateShutdownW returns a Win32 error code directly,
	// which is what the failure-log message needs, instead of requiring a separate GetLastError call
	// that may race a background shutdown-veto notification.
	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern uint InitiateShutdownW(
		string? machineName,
		string? message,
		uint gracePeriod,
		uint shutdownFlags,
		uint reason);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

	[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool AdjustTokenPrivileges(
		IntPtr tokenHandle,
		[MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
		ref TokenPrivileges newState,
		uint bufferLength,
		IntPtr previousState,
		IntPtr returnLength);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);
}
