using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("macos")]
internal static class MacOsGpuInterop
{
	private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	public static GpuSample ReadAccelerator()
	{
		// IOServiceGetMatchingServices consumes the matching dictionary, so it is never released here.
		var matching = IOServiceMatching("IOAccelerator");
		if (matching == IntPtr.Zero || IOServiceGetMatchingServices(0, matching, out var iterator) != 0)
		{
			return new GpuSample(null, null);
		}

		string? model = null;
		double? utilization = null;
		try
		{
			// The match also returns a compatibility entry without these properties, in no
			// guaranteed order, so each value comes from the first entry that carries it.
			uint entry;
			while ((entry = IOIteratorNext(iterator)) != 0)
			{
				try
				{
					model ??= ReadModel(entry);
					utilization ??= ReadUtilization(entry);
				}
				finally
				{
					_ = IOObjectRelease(entry);
				}
			}
		}
		finally
		{
			_ = IOObjectRelease(iterator);
		}

		return new GpuSample(model, utilization);
	}

	private static string? ReadModel(uint entry)
	{
		var value = CopyProperty(entry, "model");
		if (value == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			if (!MacOsCoreFoundation.IsCfString(value))
			{
				return null;
			}

			var model = MacOsCoreFoundation.ReadCfString(value)?.Trim();
			return model is { Length: > 0 } ? model : null;
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(value);
		}
	}

	private static double? ReadUtilization(uint entry)
	{
		var statistics = CopyProperty(entry, "PerformanceStatistics");
		if (statistics == IntPtr.Zero)
		{
			return null;
		}

		var key = MacOsCoreFoundation.CreateCfString("Device Utilization %");
		try
		{
			if (CFGetTypeID(statistics) != CFDictionaryGetTypeID()
				|| !MacOsCoreFoundation.TryReadCfLong(statistics, key, out var value))
			{
				return null;
			}

			return Math.Clamp(value, 0.0, 100.0);
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(key);
			MacOsCoreFoundation.CFRelease(statistics);
		}
	}

	private static IntPtr CopyProperty(uint entry, string name)
	{
		var key = MacOsCoreFoundation.CreateCfString(name);
		try
		{
			return IORegistryEntryCreateCFProperty(entry, key, IntPtr.Zero, 0);
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(key);
		}
	}

	[DllImport(IoKit, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern IntPtr IOServiceMatching(string name);

	[DllImport(IoKit)]
	private static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

	[DllImport(IoKit)]
	private static extern uint IOIteratorNext(uint iterator);

	[DllImport(IoKit)]
	private static extern IntPtr IORegistryEntryCreateCFProperty(
		uint entry,
		IntPtr key,
		IntPtr allocator,
		uint options);

	[DllImport(IoKit)]
	private static extern int IOObjectRelease(uint entry);

	[DllImport(CoreFoundation)]
	private static extern nuint CFGetTypeID(IntPtr value);

	[DllImport(CoreFoundation)]
	private static extern nuint CFDictionaryGetTypeID();
}
