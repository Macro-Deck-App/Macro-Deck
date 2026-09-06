using System.Globalization;

namespace MacroDeckHost.Integrations.Voicemeeter;

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

	public static string Strip(int index, string parameter) => $"Strip[{index}].{parameter}";

	public static string Bus(int index, string parameter) => $"Bus[{index}].{parameter}";

	public static string StripBusAssignment(int stripIndex, string busName) => Strip(stripIndex, busName);

	public static string Command(string command) => $"Command.{command}";

	public static string Recorder(string parameter) => $"Recorder.{parameter}";

	public static string FadeArgument(double decibels, int milliseconds)
		=> string.Create(CultureInfo.InvariantCulture, $"({decibels:0.0}, {milliseconds})");
}
