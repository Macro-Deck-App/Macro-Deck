using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Providers;

[SupportedOSPlatform("windows")]
internal sealed class WindowsHostBatteryReader : IHostBatteryReader
{
	public Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken)
	{
		WindowsPowerStatus? status = GetSystemPowerStatus(out var native)
			? new WindowsPowerStatus(native.AcLineStatus,
				native.BatteryFlag,
				native.BatteryLifePercent,
				native.BatteryLifeTime)
			: null;
		return Task.FromResult(WindowsPowerStatusMapper.Map(status));
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct SystemPowerStatus
	{
		public readonly byte AcLineStatus;
		public readonly byte BatteryFlag;
		public readonly byte BatteryLifePercent;
		public readonly byte SystemStatusFlag;
		public readonly uint BatteryLifeTime;
		public readonly uint BatteryFullLifeTime;
	}
}
