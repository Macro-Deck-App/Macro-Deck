using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace MacroDeckHost.Integrations.System;

internal static class SystemInfo
{
	private const string PrettyNamePrefix = "PRETTY_NAME=";
	private const string ModelNamePrefix = "model name";
	private const string MacCpuBrandKey = "machdep.cpu.brand_string";

	public static string PcName => Environment.MachineName;

	public static string OsName { get; } = ResolveOsName();

	public static string? CpuName { get; } = ResolveCpuName();

	private static string ResolveOsName()
	{
		if (OperatingSystem.IsWindows())
		{
			return Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
		}

		if (OperatingSystem.IsMacOS())
		{
			var version = Environment.OSVersion.Version;
			return $"macOS {version.Major}.{version.Minor}";
		}

		if (OperatingSystem.IsLinux())
		{
			return ReadLinuxOsName() ?? "Linux";
		}

		return RuntimeInformation.OSDescription;
	}

	private static string? ReadLinuxOsName()
	{
		try
		{
			return ParseOsReleasePrettyName(File.ReadLines("/etc/os-release"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal static string? ParseOsReleasePrettyName(IEnumerable<string> lines)
	{
		foreach (var line in lines)
		{
			if (line.StartsWith(PrettyNamePrefix, StringComparison.Ordinal))
			{
				var value = line[PrettyNamePrefix.Length..].Trim().Trim('"');
				return value.Length > 0 ? value : null;
			}
		}

		return null;
	}

	private static string? ResolveCpuName()
	{
		if (OperatingSystem.IsLinux())
		{
			return ReadLinuxCpuName();
		}

		if (OperatingSystem.IsWindows())
		{
			return ReadCpuBrandStringViaCpuId();
		}

		if (OperatingSystem.IsMacOS())
		{
			return ReadMacCpuName();
		}

		return null;
	}

	private static string? ReadLinuxCpuName()
	{
		try
		{
			return ParseCpuModelName(File.ReadLines("/proc/cpuinfo"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal static string? ParseCpuModelName(IEnumerable<string> lines)
	{
		foreach (var line in lines)
		{
			if (!line.StartsWith(ModelNamePrefix, StringComparison.Ordinal))
			{
				continue;
			}

			var colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon < 0)
			{
				continue;
			}

			var value = line[(colon + 1)..].Trim();
			if (value.Length > 0)
			{
				return value;
			}
		}

		return null;
	}

	private static string? ReadCpuBrandStringViaCpuId()
	{
		if (!X86Base.IsSupported)
		{
			return null;
		}

		var (maxExtended, _, _, _) = X86Base.CpuId(unchecked((int)0x80000000u), 0);
		if ((uint)maxExtended < 0x80000004u)
		{
			return null;
		}

		Span<byte> buffer = stackalloc byte[48];
		var offset = 0;
		for (var leaf = 0x80000002u; leaf <= 0x80000004u; leaf++)
		{
			var (eax, ebx, ecx, edx) = X86Base.CpuId(unchecked((int)leaf), 0);
			offset += WriteRegister(buffer[offset..], eax);
			offset += WriteRegister(buffer[offset..], ebx);
			offset += WriteRegister(buffer[offset..], ecx);
			offset += WriteRegister(buffer[offset..], edx);
		}

		var terminator = buffer.IndexOf((byte)0);
		var length = terminator >= 0 ? terminator : buffer.Length;
		var value = Encoding.ASCII.GetString(buffer[..length]).Trim();
		return value.Length > 0 ? value : null;

		static int WriteRegister(Span<byte> destination, int register)
		{
			BitConverter.TryWriteBytes(destination, register);
			return sizeof(int);
		}
	}

	private static string? ReadMacCpuName()
	{
		try
		{
			nuint size = 0;
			if (Sysctlbyname(MacCpuBrandKey, null, ref size, IntPtr.Zero, 0) != 0 || size == 0)
			{
				return null;
			}

			var buffer = new byte[size];
			if (Sysctlbyname(MacCpuBrandKey, buffer, ref size, IntPtr.Zero, 0) != 0)
			{
				return null;
			}

			var length = (int)size;
			if (length > 0 && buffer[length - 1] == 0)
			{
				length--;
			}

			var value = Encoding.ASCII.GetString(buffer, 0, length).Trim();
			return value.Length > 0 ? value : null;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			return null;
		}
	}

	[DllImport("libSystem.dylib",
		EntryPoint = "sysctlbyname",
		CharSet = CharSet.Ansi,
		BestFitMapping = false,
		ThrowOnUnmappableChar = true)]
	private static extern int Sysctlbyname(
		[MarshalAs(UnmanagedType.LPStr)] string name,
		byte[]? oldValue,
		ref nuint oldLength,
		IntPtr newValue,
		nuint newLength);
}
