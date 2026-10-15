using System.Globalization;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal static class VoicemeeterParameters
{
	public const string Gain = "Gain";
	public const string Label = "Label";
	public const string Mute = "Mute";
	public const string Mono = "Mono";
	public const string Solo = "Solo";
	public const string Eq = "EQ.on";
	public const string Sel = "Sel";

	public const string FadeTo = "FadeTo";

	public static string Strip(int index, string parameter)
	{
		return $"Strip[{index}].{parameter}";
	}

	public static string Bus(int index, string parameter)
	{
		return $"Bus[{index}].{parameter}";
	}

	public static string StripBusAssignment(int stripIndex, string busName)
	{
		return Strip(stripIndex, busName);
	}

	public static string Command(string command)
	{
		return $"Command.{command}";
	}

	public static string Recorder(string parameter)
	{
		return $"Recorder.{parameter}";
	}

	public static string FadeArgument(double decibels, int milliseconds)
	{
		return string.Create(CultureInfo.InvariantCulture, $"({decibels:0.0}, {milliseconds})");
	}
}
