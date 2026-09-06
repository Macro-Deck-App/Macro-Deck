using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemMetricsService : SystemMetricsServiceBase
{
	private readonly bool _hasNvidiaSmi = ProcessRunner.CommandExists("nvidia-smi");

	public override bool IsGpuSupported => _hasNvidiaSmi;

	protected override Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken)
	{
		if (!GetSystemTimes(out var idle, out var kernel, out var user))
		{
			return Task.FromResult<CpuTimes?>(null);
		}

		var idleTicks = idle.ToTicks();
		var total = kernel.ToTicks() + user.ToTicks();
		return Task.FromResult<CpuTimes?>(new CpuTimes(total - idleTicks, total));
	}

	protected override Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken)
	{
		var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
		if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
		{
			return Task.FromResult<MemoryInfo?>(null);
		}

		return Task.FromResult<MemoryInfo?>(new MemoryInfo((long)status.TotalPhys, (long)status.AvailPhys));
	}

	protected override async Task<double?> ReadGpuUsageAsync(CancellationToken cancellationToken)
	{
		if (!_hasNvidiaSmi)
		{
			return null;
		}

		var output = await ProcessRunner.RunAsync("nvidia-smi",
			["--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"],
			cancellationToken);
		return NvidiaSmiParser.ParseUtilization(output);
	}

	protected override async Task<string?> ReadGpuNameAsync(CancellationToken cancellationToken)
	{
		if (!_hasNvidiaSmi)
		{
			return null;
		}

		var output = await ProcessRunner.RunAsync("nvidia-smi",
			["--query-gpu=name", "--format=csv,noheader"],
			cancellationToken);
		return NvidiaSmiParser.ParseName(output);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct FileTime
	{
		private readonly uint _low;
		private readonly uint _high;

		public ulong ToTicks() => ((ulong)_high << 32) | _low;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}
}
