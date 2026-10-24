using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.AutoHotkey.Configuration;

internal static class AutoHotkeyConfigKeys
{
	public const string ExecutableMode = "executableMode";
	public const string ExecutablePath = "executablePath";
	public const string GracePeriod = "gracePeriod";

	public const string AutomaticMode = "auto";
	public const string ManualMode = "manual";
}

internal sealed record AutoHotkeySettings(string? ManualExecutable, TimeSpan GracePeriod)
{
	public static readonly TimeSpan MinGracePeriod = TimeSpan.FromMilliseconds(500);
	public static readonly TimeSpan MaxGracePeriod = TimeSpan.FromSeconds(5);
	public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(3);

	public static AutoHotkeySettings Default { get; } = new(null, DefaultGracePeriod);

	public static TimeSpan ParseGracePeriod(object? raw)
	{
		return Clamp(ReadNumber(raw));
	}

	public static TimeSpan ParseGracePeriodSeconds(object? raw)
	{
		return Clamp(ReadNumber(raw) * 1000);
	}

	private static double? ReadNumber(object? raw)
	{
		return raw switch
		{
			double value => value,
			float value => value,
			int value => value,
			long value => value,
			decimal value => (double)value,
			JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
			JsonElement { ValueKind: JsonValueKind.String } element => Parse(element.GetString()),
			string text => Parse(text),
			_ => null
		};
	}

	private static TimeSpan Clamp(double? milliseconds)
	{
		if (milliseconds is not { } finite || !double.IsFinite(finite))
		{
			return DefaultGracePeriod;
		}

		var span = TimeSpan.FromMilliseconds(finite);
		return span < MinGracePeriod ? MinGracePeriod : span > MaxGracePeriod ? MaxGracePeriod : span;
	}

	private static double? Parse(string? text)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
	}
}
