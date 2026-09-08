using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("macos")]
internal sealed class MacOsSystemMetricsService : SystemMetricsServiceBase
{
	private const int HostCpuLoadInfo = 3;
	private const int HostVmInfo64 = 4;
	private const int CpuStateCount = 4;
	private const int VmInfo64Count = 38;

	private const int VmFreeIndex = 0;
	private const int VmInactiveIndex = 2;
	private const int VmPurgeableIndex = 22;

	public override int GpuCount => 1;

	protected override Task<CpuTimes?> ReadCpuTimesAsync(CancellationToken cancellationToken)
	{
		var info = new uint[CpuStateCount];
		var count = (uint)CpuStateCount;
		if (host_statistics(mach_host_self(), HostCpuLoadInfo, info, ref count) != 0)
		{
			return Task.FromResult<CpuTimes?>(null);
		}

		var busy = (ulong)info[0] + info[1] + info[3];
		var total = busy + info[2];
		return Task.FromResult<CpuTimes?>(new CpuTimes(busy, total));
	}

	protected override Task<MemoryInfo?> ReadMemoryAsync(CancellationToken cancellationToken)
	{
		var totalBytes = ReadTotalMemoryBytes();
		if (totalBytes is not { } total)
		{
			return Task.FromResult<MemoryInfo?>(null);
		}

		var host = mach_host_self();
		if (host_page_size(host, out var pageSize) != 0)
		{
			return Task.FromResult<MemoryInfo?>(null);
		}

		var info = new uint[VmInfo64Count];
		var count = (uint)VmInfo64Count;
		if (host_statistics64(host, HostVmInfo64, info, ref count) != 0)
		{
			return Task.FromResult<MemoryInfo?>(null);
		}

		var availablePages = (ulong)info[VmFreeIndex] + info[VmInactiveIndex] + info[VmPurgeableIndex];
		var available = (long)Math.Min(availablePages * pageSize, (ulong)total);
		return Task.FromResult<MemoryInfo?>(new MemoryInfo(total, available));
	}

	protected override async Task<IReadOnlyList<GpuSample>> ReadGpuSnapshotAsync(CancellationToken cancellationToken)
	{
		var output = await ReadIoRegAcceleratorAsync(cancellationToken);
		return
		[
			new GpuSample(
				MacOsIoRegParser.ParseAcceleratorModel(output),
				MacOsIoRegParser.ParseDeviceUtilization(output))
		];
	}

	private static Task<string> ReadIoRegAcceleratorAsync(CancellationToken cancellationToken)
		=> ProcessRunner.RunAsync("ioreg",
			["-r", "-d", "1", "-w", "0", "-c", "IOAccelerator"],
			cancellationToken);

	private static long? ReadTotalMemoryBytes()
	{
		var value = 0L;
		var size = (nuint)sizeof(long);
		if (sysctlbyname("hw.memsize", ref value, ref size, IntPtr.Zero, 0) != 0 || value <= 0)
		{
			return null;
		}

		return value;
	}

	[DllImport("libSystem.dylib")]
	private static extern IntPtr mach_host_self();

	[DllImport("libSystem.dylib")]
	private static extern int host_statistics(IntPtr host, int flavor, uint[] info, ref uint count);

	[DllImport("libSystem.dylib")]
	private static extern int host_statistics64(IntPtr host, int flavor, uint[] info, ref uint count);

	[DllImport("libSystem.dylib")]
	private static extern int host_page_size(IntPtr host, out nuint pageSize);

	[DllImport("libSystem.dylib", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int sysctlbyname(
		[MarshalAs(UnmanagedType.LPStr)] string name,
		ref long value,
		ref nuint size,
		IntPtr newValue,
		nuint newLength);
}
