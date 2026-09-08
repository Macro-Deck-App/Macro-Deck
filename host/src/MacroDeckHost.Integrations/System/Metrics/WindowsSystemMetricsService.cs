using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemMetricsService : SystemMetricsServiceBase
{
	private static readonly ILogger _logger =
		IntegrationLog.For<WindowsSystemMetricsService>("app.macro-deck.system");

	private readonly bool _hasNvidiaSmi = ProcessRunner.CommandExists("nvidia-smi");
	private readonly WindowsGpuInterop _interop = new();
	private readonly IReadOnlyList<WindowsGpuAdapter> _adapters;

	private bool _loggedEmptyCounters;
	private bool _loggedLuidFallback;

	public WindowsSystemMetricsService()
	{
		_adapters = WindowsGpuSnapshotBuilder.Surviving(WindowsGpuInterop.EnumerateAdapters());
	}

	public override int GpuCount => _adapters.Count > 0 ? _adapters.Count : _hasNvidiaSmi ? 1 : 0;

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

	protected override async Task<IReadOnlyList<GpuSample>> ReadGpuSnapshotAsync(CancellationToken cancellationToken)
	{
		var counters = _interop.ReadCounters();
		LogEmptyCountersOnce(counters);

		var nvidia = WindowsGpuSnapshotBuilder.NeedsNvidiaFallback(_adapters, counters, _hasNvidiaSmi)
			? await ReadNvidiaSmiAsync(cancellationToken)
			: null;

		var snapshot = WindowsGpuSnapshotBuilder.Build(_adapters, counters, nvidia, out var join);
		LogLuidFallbackOnce(join);
		return snapshot;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_interop.Dispose();
		}

		base.Dispose(disposing);
	}

	private static async Task<string?> ReadNvidiaSmiAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await ProcessRunner.RunAsync("nvidia-smi",
				["--query-gpu=index,name,utilization.gpu", "--format=csv,noheader,nounits"],
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	private void LogEmptyCountersOnce(CounterReadResult counters)
	{
		if (_loggedEmptyCounters || counters is not (CounterReadResult.Failed or CounterReadResult.Instances([])))
		{
			return;
		}

		_loggedEmptyCounters = true;
		_logger.Warning(
			"The GPU Engine performance counters returned nothing ({Result}); GPU usage falls back to nvidia-smi or reads as unavailable",
			counters.GetType().Name);
	}

	private void LogLuidFallbackOnce(WindowsGpuSnapshotBuilder.LuidJoin join)
	{
		if (_loggedLuidFallback || join == WindowsGpuSnapshotBuilder.LuidJoin.Matched)
		{
			return;
		}

		_loggedLuidFallback = true;
		_logger.Warning("GPU counter instances did not join to the DXGI adapters as expected ({Join})", join);
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
