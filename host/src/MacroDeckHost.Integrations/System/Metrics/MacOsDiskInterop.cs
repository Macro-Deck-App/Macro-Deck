using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Metrics;

[SupportedOSPlatform("macos")]
internal static class MacOsDiskInterop
{
	private const string LibSystem = "libSystem.dylib";
	private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	// struct statfs with 64-bit inodes, the only layout on arm64 (the only macOS build that ships).
	private const int StatFsSize = 2168;
	private const int BlockSizeOffset = 0;
	private const int BlocksOffset = 8;
	private const int AvailableBlocksOffset = 24;
	private const int FlagsOffset = 64;
	private const int FileSystemTypeOffset = 72;
	private const int FileSystemTypeLength = 16;
	private const int MountOnOffset = 88;
	private const int MountFromOffset = 1112;
	private const int PathLength = 1024;

	private const int MntNoWait = 2;
	private const uint MntLocal = 0x00001000;
	private const uint MntDontBrowse = 0x00100000;

	private const string DevicePrefix = "/dev/";

	public static IReadOnlyList<DiskReading> ReadDisks()
	{
		var readings = new List<DiskReading>();
		foreach (var volume in ListBrowsableLocalVolumes())
		{
			var (total, free) = ReadCapacity(volume.MountPoint);
			readings.Add(new DiskReading(volume.MountPoint,
				ReadVolumeName(volume.MountPoint) ?? DefaultName(volume.MountPoint),
				volume.FileSystem,
				total,
				free,
				volume.Device.StartsWith(DevicePrefix, StringComparison.Ordinal)
					? ReadCounters(volume.Device[DevicePrefix.Length..])
					: null));
		}

		return readings;
	}

	private static string DefaultName(string mountPoint)
		=> mountPoint == "/" ? mountPoint : Path.GetFileName(mountPoint.TrimEnd('/'));

	private static List<(string MountPoint, string FileSystem, string Device)> ListBrowsableLocalVolumes()
	{
		var volumes = new List<(string, string, string)>();
		var count = getfsstat(IntPtr.Zero, 0, MntNoWait);
		if (count <= 0)
		{
			return volumes;
		}

		var size = (count + 4) * StatFsSize;
		var buffer = Marshal.AllocHGlobal(size);
		try
		{
			count = getfsstat(buffer, size, MntNoWait);
			for (var index = 0; index < count; index++)
			{
				var entry = buffer + (index * StatFsSize);
				var flags = (uint)Marshal.ReadInt32(entry, FlagsOffset);
				if ((flags & MntLocal) == 0 || (flags & MntDontBrowse) != 0)
				{
					continue;
				}

				volumes.Add((ReadCString(entry + MountOnOffset, PathLength),
					ReadCString(entry + FileSystemTypeOffset, FileSystemTypeLength),
					ReadCString(entry + MountFromOffset, PathLength)));
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}

		return volumes;
	}

	private static (long? Total, long? Free) ReadCapacity(string mountPoint)
	{
		var buffer = Marshal.AllocHGlobal(StatFsSize);
		try
		{
			if (statfs(mountPoint, buffer) != 0)
			{
				return (null, null);
			}

			var blockSize = (long)(uint)Marshal.ReadInt32(buffer, BlockSizeOffset);
			return (Marshal.ReadInt64(buffer, BlocksOffset) * blockSize,
				Marshal.ReadInt64(buffer, AvailableBlocksOffset) * blockSize);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	private static string? ReadVolumeName(string mountPoint)
	{
		var path = Encoding.UTF8.GetBytes(mountPoint);
		var url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, true);
		if (url == IntPtr.Zero)
		{
			return null;
		}

		var key = MacOsCoreFoundation.CreateCFString("NSURLVolumeNameKey");
		try
		{
			if (!CFURLCopyResourcePropertyForKey(url, key, out var value, IntPtr.Zero) || value == IntPtr.Zero)
			{
				return null;
			}

			try
			{
				return MacOsCoreFoundation.IsCFString(value) && MacOsCoreFoundation.ReadCFString(value) is { Length: > 0 } name
					? name
					: null;
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(value);
			}
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(key);
			MacOsCoreFoundation.CFRelease(url);
		}
	}

	// An APFS volume sits several levels below the physical disk's IOBlockStorageDriver, which is the
	// only node that keeps I/O statistics, so the counters are those of the whole physical disk.
	private static DiskCounters? ReadCounters(string bsdName)
	{
		var service = IOServiceGetMatchingService(0, IOBSDNameMatching(0, 0, bsdName));
		if (service == 0)
		{
			return null;
		}

		var entry = service;
		try
		{
			while (!IOObjectConformsTo(entry, "IOBlockStorageDriver"))
			{
				if (IORegistryEntryGetParentEntry(entry, "IOService", out var parent) != 0 || parent == 0)
				{
					return null;
				}

				_ = IOObjectRelease(entry);
				entry = parent;
			}

			return ReadStatistics(entry);
		}
		finally
		{
			_ = IOObjectRelease(entry);
		}
	}

	private static DiskCounters? ReadStatistics(uint driver)
	{
		var statisticsKey = MacOsCoreFoundation.CreateCFString("Statistics");
		var statistics = IORegistryEntryCreateCFProperty(driver, statisticsKey, IntPtr.Zero, 0);
		MacOsCoreFoundation.CFRelease(statisticsKey);
		if (statistics == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var bytesRead = ReadLong(statistics, "Bytes (Read)");
			var bytesWritten = ReadLong(statistics, "Bytes (Write)");
			if (bytesRead is not { } read || bytesWritten is not { } written)
			{
				return null;
			}

			return new DiskCounters((ulong)read,
				(ulong)written,
				Nanoseconds(ReadLong(statistics, "Total Time (Read)")),
				Nanoseconds(ReadLong(statistics, "Total Time (Write)")));
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(statistics);
		}
	}

	private static long? ReadLong(IntPtr dictionary, string name)
	{
		var key = MacOsCoreFoundation.CreateCFString(name);
		try
		{
			return MacOsCoreFoundation.TryReadCFLong(dictionary, key, out var value) && value >= 0 ? value : null;
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(key);
		}
	}

	private static TimeSpan? Nanoseconds(long? value)
		=> value is { } nanoseconds ? TimeSpan.FromTicks(nanoseconds / 100) : null;

	private static string ReadCString(IntPtr pointer, int maxLength)
	{
		var bytes = new byte[maxLength];
		Marshal.Copy(pointer, bytes, 0, maxLength);
		var length = Array.IndexOf(bytes, (byte)0);
		return Encoding.UTF8.GetString(bytes, 0, length < 0 ? maxLength : length);
	}

	[DllImport(LibSystem, SetLastError = true)]
	private static extern int getfsstat(IntPtr buffer, int bufferSize, int flags);

	[DllImport(LibSystem, SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int statfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFURLCreateFromFileSystemRepresentation(
		IntPtr allocator,
		byte[] buffer,
		nint length,
		[MarshalAs(UnmanagedType.I1)] bool isDirectory);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFURLCopyResourcePropertyForKey(IntPtr url, IntPtr key, out IntPtr value, IntPtr error);

	[DllImport(IoKit, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern IntPtr IOBSDNameMatching(uint mainPort, uint options, string bsdName);

	[DllImport(IoKit)]
	private static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

	[DllImport(IoKit, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	private static extern int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

	[DllImport(IoKit, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IOObjectConformsTo(uint entry, string className);

	[DllImport(IoKit)]
	private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

	[DllImport(IoKit)]
	private static extern int IOObjectRelease(uint entry);
}
