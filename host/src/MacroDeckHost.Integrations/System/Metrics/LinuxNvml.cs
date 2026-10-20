using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNvml
{
	private const string Library = "libnvidia-ml.so.1";

	private const int Success = 0;
	private const int ErrorUninitialized = 1;
	private const int ErrorDriverNotLoaded = 9;
	private const int ErrorGpuIsLost = 15;
	private const int ErrorUnknown = 999;
	private const int NameBufferSize = 96;
	private const long RetryDelayMs = 60_000;

	private bool _initialized;
	private long? _failedAt;

	public IReadOnlyList<GpuSample>? TryRead(int count)
	{
		if (!_initialized && !TryInitialize())
		{
			return null;
		}

		var samples = new GpuSample[count];
		for (var index = 0; index < count; index++)
		{
			var handleResult = nvmlDeviceGetHandleByIndex_v2((uint)index, out var device);
			if (handleResult != Success)
			{
				if (IsDeviceWide(handleResult))
				{
					Fail();
					return null;
				}

				samples[index] = new GpuSample(null, null);
				continue;
			}

			var utilizationResult = nvmlDeviceGetUtilizationRates(device, out var utilization);
			if (IsDeviceWide(utilizationResult))
			{
				Fail();
				return null;
			}

			samples[index] = new GpuSample(ReadName(device),
				utilizationResult == Success ? Math.Min(utilization.Gpu, 100u) : null);
		}

		return samples;
	}

	private bool TryInitialize()
	{
		if (_failedAt is { } failedAt && Environment.TickCount64 - failedAt < RetryDelayMs)
		{
			return false;
		}

		try
		{
			if (nvmlInit_v2() == Success)
			{
				_initialized = true;
				return true;
			}
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
		}

		_failedAt = Environment.TickCount64;
		return false;
	}

	private void Fail()
	{
		_ = nvmlShutdown();
		_initialized = false;
		_failedAt = Environment.TickCount64;
	}

	private static string? ReadName(nint device)
	{
		var buffer = new byte[NameBufferSize];
		if (nvmlDeviceGetName(device, buffer, NameBufferSize) != Success)
		{
			return null;
		}

		var length = Array.IndexOf(buffer, (byte)0);
		var name = Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length).Trim();
		return name.Length > 0 ? name : null;
	}

	private static bool IsDeviceWide(int result)
	{
		return result is ErrorUninitialized or ErrorDriverNotLoaded or ErrorGpuIsLost or ErrorUnknown;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Utilization
	{
		public uint Gpu;
		public uint Memory;
	}

	[DllImport(Library)]
	private static extern int nvmlInit_v2();

	[DllImport(Library)]
	private static extern int nvmlShutdown();

	[DllImport(Library)]
	private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);

	[DllImport(Library)]
	private static extern int nvmlDeviceGetName(nint device, byte[] name, uint length);

	[DllImport(Library)]
	private static extern int nvmlDeviceGetUtilizationRates(nint device, out Utilization utilization);
}
