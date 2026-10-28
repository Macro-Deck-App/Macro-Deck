using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Platform.Native;

// Each reader answers with the system's short time pattern; a forced clock is answered as a pattern of that
// cycle, so the host classifies every answer the same way it classifies a culture's pattern.
public static class SystemHourCycleReaderFactory
{
	internal const string TwelveHourPattern = "h:mm tt";
	internal const string TwentyFourHourPattern = "HH:mm";

	public static Func<string?> CreatePatternReader()
	{
		if (OperatingSystem.IsWindows())
		{
			return WindowsHourCycleReader.Read;
		}

		if (OperatingSystem.IsMacOS())
		{
			return MacOsHourCycleReader.Read;
		}

		return new LinuxHourCycleReader().Read;
	}
}

[SupportedOSPlatform("macos")]
internal static class MacOsHourCycleReader
{
	private const nint NoStyle = 0;
	private const nint ShortStyle = 1;

	public static string? Read()
	{
		var anyApplication = MacOsCoreFoundation.CreateCfString("kCFPreferencesAnyApplication");
		try
		{
			MacOsCoreFoundation.CFPreferencesAppSynchronize(anyApplication);

			if (IsSet("AppleICUForce24HourTime", anyApplication))
			{
				return SystemHourCycleReaderFactory.TwentyFourHourPattern;
			}

			if (IsSet("AppleICUForce12HourTime", anyApplication))
			{
				return SystemHourCycleReaderFactory.TwelveHourPattern;
			}

			return ShortTimePattern(anyApplication);
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(anyApplication);
		}
	}

	private static bool IsSet(string key, IntPtr application)
	{
		var name = MacOsCoreFoundation.CreateCfString(key);
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
		var key = MacOsCoreFoundation.CreateCfString("AppleLocale");
		var identifier = MacOsCoreFoundation.CFPreferencesCopyAppValue(key, application);
		MacOsCoreFoundation.CFRelease(key);

		// CFLocaleCopyCurrent is cached for the process, so the stored identifier is preferred to see a
		// region change made while the host runs.
		var locale = identifier != IntPtr.Zero && MacOsCoreFoundation.IsCfString(identifier)
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
			return MacOsCoreFoundation.ReadCfString(MacOsCoreFoundation.CFDateFormatterGetFormat(formatter));
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(formatter);
		}
	}
}

[SupportedOSPlatform("windows")]
internal static class WindowsHourCycleReader
{
	private const uint LocaleShortTime = 0x79;

	public static string? Read()
	{
		var buffer = new char[80];
		var length = GetLocaleInfoEx(null, LocaleShortTime, buffer, buffer.Length);

		return length > 1 ? new string(buffer, 0, length - 1) : null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetLocaleInfoEx(string? localeName, uint lcType, [Out] char[] data, int dataLength);
}

internal sealed class LinuxHourCycleReader
{
	private static readonly TimeSpan _gsettingsTimeout = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _gnomeCacheLifetime = TimeSpan.FromSeconds(60);
	private static readonly string[] _localeVariables = ["LC_ALL", "LC_TIME", "LANG"];
	private static readonly char[] _localeSuffixes = ['.', '@'];

	private readonly global::System.Threading.Lock _gnomeLock = new();
	private long? _gnomeReadAtMs;
	private string? _gnomePattern;

	public string? Read()
	{
		return CachedGnomeClockFormat() ?? LocalePattern();
	}

	private string? CachedGnomeClockFormat()
	{
		lock (_gnomeLock)
		{
			var now = Environment.TickCount64;
			if (_gnomeReadAtMs is not { } readAt || now - readAt >= (long)_gnomeCacheLifetime.TotalMilliseconds)
			{
				_gnomePattern = GnomeClockFormat();
				_gnomeReadAtMs = now;
			}

			return _gnomePattern;
		}
	}

	internal static string? LocalePattern()
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
			return CultureInfo.GetCultureInfo(name, true).DateTimeFormat.ShortTimePattern;
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
		AppImageEnvironment.RestoreOutside(startInfo);

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
				process.Kill(true);
				return null;
			}

			return output.GetAwaiter().GetResult().Trim() switch
			{
				"'12h'" => SystemHourCycleReaderFactory.TwelveHourPattern,
				"'24h'" => SystemHourCycleReaderFactory.TwentyFourHourPattern,
				_ => null
			};
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
		{
			return null;
		}
	}
}
