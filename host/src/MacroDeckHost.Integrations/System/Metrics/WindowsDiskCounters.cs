using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("windows")]
internal sealed class WindowsDiskCounters : IDisposable
{
	// English paths, because counter set names are localized and would not resolve on a localized machine.
	private static readonly string[] _counterPaths =
	[
		@"\LogicalDisk(*)\Disk Read Bytes/sec",
		@"\LogicalDisk(*)\Disk Write Bytes/sec",
		@"\LogicalDisk(*)\% Disk Read Time",
		@"\LogicalDisk(*)\% Disk Write Time"
	];

	private const uint PdhFmtDouble = 0x00000200;
	private const uint PdhFmtNoCap100 = 0x00008000;
	private const int PdhMoreData = unchecked((int)0x800007D2);
	private const int PdhCStatusValidData = 0x00000000;
	private const int PdhCStatusNewData = 0x00000001;

	private readonly nint[] _counters = new nint[_counterPaths.Length];
	private nint _query;
	private bool _queryFailed;
	private bool _primed;
	private bool _disposed;

	~WindowsDiskCounters() => Close();

	public IReadOnlyDictionary<string, DiskActivity>? Read()
	{
		try
		{
			return ReadCore();
		}
		catch
		{
			return null;
		}
	}

	public void Dispose()
	{
		Close();
		GC.SuppressFinalize(this);
	}

	private Dictionary<string, DiskActivity>? ReadCore()
	{
		if (_queryFailed || _disposed || (_query == 0 && !OpenQuery()) || PdhCollectQueryData(_query) != 0)
		{
			return null;
		}

		if (!_primed)
		{
			_primed = true;
			return null;
		}

		var values = _counters.Select(FormatCounterArray).ToArray();
		var activity = new Dictionary<string, DiskActivity>(StringComparer.OrdinalIgnoreCase);
		foreach (var (instance, readBytes) in values[0])
		{
			if (!values[1].TryGetValue(instance, out var writeBytes))
			{
				continue;
			}

			activity[instance] = new DiskActivity(Math.Max(0, readBytes),
				Math.Max(0, writeBytes),
				values[2].TryGetValue(instance, out var readTime) ? DiskRateCalculator.ClampPercent(readTime) : null,
				values[3].TryGetValue(instance, out var writeTime) ? DiskRateCalculator.ClampPercent(writeTime) : null);
		}

		return activity;
	}

	private bool OpenQuery()
	{
		if (PdhOpenQuery(null, nint.Zero, out var query) != 0)
		{
			_queryFailed = true;
			return false;
		}

		for (var index = 0; index < _counterPaths.Length; index++)
		{
			if (PdhAddEnglishCounter(query, _counterPaths[index], nint.Zero, out _counters[index]) != 0)
			{
				_ = PdhCloseQuery(query);
				_queryFailed = true;
				return false;
			}
		}

		_query = query;
		return true;
	}

	private static Dictionary<string, double> FormatCounterArray(nint counter)
	{
		var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		var bufferSize = 0u;
		if (PdhGetFormattedCounterArray(counter, PdhFmtDouble | PdhFmtNoCap100, ref bufferSize, out _, nint.Zero) !=
			PdhMoreData ||
			bufferSize == 0)
		{
			return values;
		}

		var buffer = Marshal.AllocHGlobal((int)bufferSize);
		try
		{
			if (PdhGetFormattedCounterArray(counter,
					PdhFmtDouble | PdhFmtNoCap100,
					ref bufferSize,
					out var itemCount,
					buffer) !=
				0)
			{
				return values;
			}

			var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
			for (var i = 0; i < itemCount; i++)
			{
				var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + (i * itemSize));
				if (item.CStatus is (PdhCStatusValidData or PdhCStatusNewData) &&
					item.Name != nint.Zero &&
					Marshal.PtrToStringUni(item.Name) is { Length: > 0 } name)
				{
					values[name] = item.DoubleValue;
				}
			}

			return values;
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	private void Close()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_query != 0)
		{
			_ = PdhCloseQuery(_query);
			_query = 0;
		}
	}

	[DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
	private static extern int PdhOpenQuery(string? dataSource, nint userData, out nint query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
	private static extern int PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

	[DllImport("pdh.dll")]
	private static extern int PdhCollectQueryData(nint query);

	[DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
	private static extern int PdhGetFormattedCounterArray(
		nint counter,
		uint format,
		ref uint bufferSize,
		out uint itemCount,
		nint items);

	[DllImport("pdh.dll")]
	private static extern int PdhCloseQuery(nint query);

	[StructLayout(LayoutKind.Sequential)]
	private struct PdhFmtCounterValueItem
	{
		public nint Name;
		public int CStatus;
		private readonly int _padding;
		public double DoubleValue;
	}
}
