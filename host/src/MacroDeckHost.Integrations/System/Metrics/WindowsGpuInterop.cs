using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("windows")]
internal sealed class WindowsGpuInterop : IDisposable
{
	private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";

	private const uint PdhFmtDouble = 0x00000200;
	private const uint PdhFmtNoCap100 = 0x00008000;
	private const int PdhMoreData = unchecked((int)0x800007D2);
	private const int PdhCStatusValidData = 0x00000000;
	private const int PdhCStatusNewData = 0x00000001;
	private const int DxgiAdapterFlagSoftware = 2;

	private static readonly Guid _factory1Iid = new("770aae78-f26f-4dba-a829-253c83d1b387");

	private nint _query;
	private nint _counter;
	private bool _queryFailed;
	private bool _primed;
	private bool _disposed;

	public static IReadOnlyList<WindowsGpuAdapter> EnumerateAdapters()
	{
		// A driver-state HRESULT or a missing DXGI must not escape: this runs from the metrics
		// service constructor, and IntegrationDiscovery drops the whole System integration on a throw.
		try
		{
			return EnumerateAdaptersCore();
		}
		catch
		{
			return [];
		}
	}

	public CounterReadResult ReadCounters()
	{
		try
		{
			return ReadCountersCore();
		}
		catch
		{
			return new CounterReadResult.Failed();
		}
	}

	~WindowsGpuInterop() => Close();

	public void Dispose()
	{
		Close();
		GC.SuppressFinalize(this);
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

	private static List<WindowsGpuAdapter> EnumerateAdaptersCore()
	{
		Marshal.ThrowExceptionForHR(CreateDXGIFactory1(in _factory1Iid, out var factoryPtr));
		var factory = (IDxgiFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
		Marshal.Release(factoryPtr);

		try
		{
			var adapters = new List<WindowsGpuAdapter>();
			for (var index = 0u; factory.EnumAdapters1(index, out var adapter) == 0; index++)
			{
				try
				{
					if (adapter.GetDesc1(out var description) != 0)
					{
						continue;
					}

					adapters.Add(new WindowsGpuAdapter(
						description.Description ?? string.Empty,
						description.AdapterLuidHigh,
						description.AdapterLuidLow,
						(ulong)description.DedicatedVideoMemory,
						(ulong)description.SharedSystemMemory,
						(description.Flags & DxgiAdapterFlagSoftware) != 0));
				}
				finally
				{
					Marshal.ReleaseComObject(adapter);
				}
			}

			return adapters;
		}
		finally
		{
			Marshal.ReleaseComObject(factory);
		}
	}

	private CounterReadResult ReadCountersCore()
	{
		if (_queryFailed || _disposed)
		{
			return new CounterReadResult.Failed();
		}

		if (_query == 0 && !OpenQuery())
		{
			return new CounterReadResult.Failed();
		}

		if (PdhCollectQueryData(_query) != 0)
		{
			return new CounterReadResult.Failed();
		}

		// The first collect of a time-based counter spans no interval, so it is not a failure.
		if (!_primed)
		{
			_primed = true;
			return new CounterReadResult.FirstCollect();
		}

		return new CounterReadResult.Instances(FormatCounterArray());
	}

	private bool OpenQuery()
	{
		if (PdhOpenQuery(null, nint.Zero, out var query) != 0)
		{
			_queryFailed = true;
			return false;
		}

		// The English counter path, because the "GPU Engine" counter set name is localized and a
		// localized machine would otherwise never resolve it.
		if (PdhAddEnglishCounter(query, CounterPath, nint.Zero, out var counter) != 0)
		{
			_ = PdhCloseQuery(query);
			_queryFailed = true;
			return false;
		}

		_query = query;
		_counter = counter;
		return true;
	}

	private List<CounterEntry> FormatCounterArray()
	{
		var bufferSize = 0u;
		var itemCount = 0u;
		if (PdhGetFormattedCounterArray(_counter, PdhFmtDouble | PdhFmtNoCap100,
				ref bufferSize, out itemCount, nint.Zero) != PdhMoreData || bufferSize == 0)
		{
			return [];
		}

		var buffer = Marshal.AllocHGlobal((int)bufferSize);
		try
		{
			if (PdhGetFormattedCounterArray(_counter, PdhFmtDouble | PdhFmtNoCap100,
					ref bufferSize, out itemCount, buffer) != 0)
			{
				return [];
			}

			var entries = new List<CounterEntry>((int)itemCount);
			var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
			for (var i = 0; i < itemCount; i++)
			{
				var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + (i * itemSize));
				if (item.CStatus is not (PdhCStatusValidData or PdhCStatusNewData) || item.Name == nint.Zero)
				{
					continue;
				}

				if (Marshal.PtrToStringUni(item.Name) is { Length: > 0 } name)
				{
					entries.Add(new CounterEntry(name, item.DoubleValue));
				}
			}

			return entries;
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);

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

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DxgiAdapterDesc1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;
		public uint VendorId;
		public uint DeviceId;
		public uint SubSysId;
		public uint Revision;
		public nuint DedicatedVideoMemory;
		public nuint DedicatedSystemMemory;
		public nuint SharedSystemMemory;
		public uint AdapterLuidLow;
		public int AdapterLuidHigh;
		public int Flags;
	}

	[ComImport]
	[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDxgiFactory1
	{
		void SetPrivateData();
		void SetPrivateDataInterface();
		void GetPrivateData();
		void GetParent();
		[PreserveSig]
		int EnumAdapters(uint index, out nint adapter);
		[PreserveSig]
		int MakeWindowAssociation(nint windowHandle, uint flags);
		[PreserveSig]
		int GetWindowAssociation(out nint windowHandle);
		[PreserveSig]
		int CreateSwapChain(nint device, nint description, out nint swapChain);
		[PreserveSig]
		int CreateSoftwareAdapter(nint module, out nint adapter);
		[PreserveSig]
		int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1 adapter);
		[PreserveSig]
		int IsCurrent();
	}

	[ComImport]
	[Guid("29038f61-3839-4626-91fd-086879011a05")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IDxgiAdapter1
	{
		void SetPrivateData();
		void SetPrivateDataInterface();
		void GetPrivateData();
		void GetParent();
		[PreserveSig]
		int EnumOutputs(uint index, out nint output);
		[PreserveSig]
		int GetDesc(out nint description);
		[PreserveSig]
		int CheckInterfaceSupport(in Guid guid, out long version);
		[PreserveSig]
		int GetDesc1(out DxgiAdapterDesc1 description);
	}
}
