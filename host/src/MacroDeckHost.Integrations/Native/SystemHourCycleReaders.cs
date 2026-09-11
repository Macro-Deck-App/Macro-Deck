using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Integrations.Native;

public static class SystemHourCycleReaderFactory
{
	public static ISystemHourCycleReader Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsHourCycleReader();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsHourCycleReader();
		}

		return new LinuxHourCycleReader();
	}
}

[SupportedOSPlatform("macos")]
internal sealed class MacOsHourCycleReader : ISystemHourCycleReader
{
	private const nint NoStyle = 0;
	private const nint ShortStyle = 1;

	public string? Read()
	{
		var anyApplication = MacOsCoreFoundation.CreateCFString("kCFPreferencesAnyApplication");
		try
		{
			MacOsCoreFoundation.CFPreferencesAppSynchronize(anyApplication);

			if (IsSet("AppleICUForce24HourTime", anyApplication))
			{
				return HourCycles.H23;
			}

			if (IsSet("AppleICUForce12HourTime", anyApplication))
			{
				return HourCycles.H12;
			}

			return HourCycles.FromPattern(ShortTimePattern(anyApplication));
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(anyApplication);
		}
	}

	private static bool IsSet(string key, IntPtr application)
	{
		var name = MacOsCoreFoundation.CreateCFString(key);
		try
		{
			return MacOsCoreFoundation.CFPreferencesGetAppBooleanValue(name, application, IntPtr.Zero);
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(name);
		}
	}

	private static string? ShortTimePattern(IntPtr application)
	{
		var key = MacOsCoreFoundation.CreateCFString("AppleLocale");
		var identifier = MacOsCoreFoundation.CFPreferencesCopyAppValue(key, application);
		MacOsCoreFoundation.CFRelease(key);

		// CFLocaleCopyCurrent is cached for the process, so the stored identifier is preferred to see a
		// region change made while the host runs.
		var locale = identifier != IntPtr.Zero && MacOsCoreFoundation.IsCFString(identifier)
			? MacOsCoreFoundation.CFLocaleCreate(IntPtr.Zero, identifier)
			: MacOsCoreFoundation.CFLocaleCopyCurrent();
		if (identifier != IntPtr.Zero)
		{
			MacOsCoreFoundation.CFRelease(identifier);
		}

		if (locale == IntPtr.Zero)
		{
			return null;
		}

		var formatter = MacOsCoreFoundation.CFDateFormatterCreate(IntPtr.Zero, locale, NoStyle, ShortStyle);
		MacOsCoreFoundation.CFRelease(locale);
		if (formatter == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			return MacOsCoreFoundation.ReadCFString(MacOsCoreFoundation.CFDateFormatterGetFormat(formatter));
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(formatter);
		}
	}
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsHourCycleReader : ISystemHourCycleReader
{
	private const uint LocaleShortTime = 0x79;

	public string? Read()
	{
		var buffer = new char[80];
		var length = GetLocaleInfoEx(null, LocaleShortTime, buffer, buffer.Length);

		return length > 1 ? HourCycles.FromPattern(new string(buffer, 0, length - 1)) : null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetLocaleInfoEx(string? localeName, uint lcType, [Out] char[] data, int dataLength);
}

internal sealed class LinuxHourCycleReader : ISystemHourCycleReader
{
	private static readonly TimeSpan _gsettingsTimeout = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _gnomeCacheLifetime = TimeSpan.FromSeconds(60);
	private static readonly string[] _localeVariables = ["LC_ALL", "LC_TIME", "LANG"];
	private static readonly char[] _localeSuffixes = ['.', '@'];

	private readonly Lock _gnomeLock = new();
	private long? _gnomeReadAtMs;
	private string? _gnomeHourCycle;

	public string? Read() => CachedGnomeClockFormat() ?? LocaleHourCycle();

	private string? CachedGnomeClockFormat()
	{
		lock (_gnomeLock)
		{
			var now = Environment.TickCount64;
			if (_gnomeReadAtMs is not { } readAt || now - readAt >= (long)_gnomeCacheLifetime.TotalMilliseconds)
			{
				_gnomeHourCycle = GnomeClockFormat();
				_gnomeReadAtMs = now;
			}

			return _gnomeHourCycle;
		}
	}

	internal static string? LocaleHourCycle()
	{
		var name = NormalizeLocale(_localeVariables
			.Select(Environment.GetEnvironmentVariable)
			.FirstOrDefault(value => !string.IsNullOrEmpty(value)));
		if (name is null)
		{
			return null;
		}

		try
		{
			return HourCycles.FromPattern(
				CultureInfo.GetCultureInfo(name, predefinedOnly: true).DateTimeFormat.ShortTimePattern);
		}
		catch (CultureNotFoundException)
		{
			return null;
		}
	}

	internal static string? NormalizeLocale(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}

		var end = value.IndexOfAny(_localeSuffixes);
		var name = (end >= 0 ? value[..end] : value).Replace('_', '-');

		return name is "" or "C" or "POSIX" ? null : name;
	}

	private static string? GnomeClockFormat()
	{
		var startInfo = new ProcessStartInfo("gsettings")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = false,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("get");
		startInfo.ArgumentList.Add("org.gnome.desktop.interface");
		startInfo.ArgumentList.Add("clock-format");

		try
		{
			using var process = Process.Start(startInfo);
			if (process is null)
			{
				return null;
			}

			var output = process.StandardOutput.ReadToEndAsync();
			if (!process.WaitForExit(_gsettingsTimeout))
			{
				process.Kill(entireProcessTree: true);
				return null;
			}

			return output.GetAwaiter().GetResult().Trim() switch
			{
				"'12h'" => HourCycles.H12,
				"'24h'" => HourCycles.H23,
				_ => null
			};
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
		{
			return null;
		}
	}
}
