using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal enum SwitchMode
{
	On,
	Off,
	Toggle
}

internal static class VoicemeeterActionValues
{
	public const string StripParameter = "strip";
	public const string BusParameter = "bus";
	public const string ModeParameter = "mode";
	public const string SwitchParameter = "switch";
	public const string GainParameter = "gain";
	public const string FadeParameter = "fade";

	public static readonly IReadOnlyList<ActionParameterOption> SwitchModeOptions =
	[
		new() { Value = "toggle", Label = AppStrings.Integrations.Voicemeeter.Common.ToggleOption() },
		new() { Value = "on", Label = AppStrings.Integrations.Voicemeeter.Common.OnOption() },
		new() { Value = "off", Label = AppStrings.Integrations.Voicemeeter.Common.OffOption() }
	];

	public static double? ReadNumber(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadNumber(parameters.GetValueOrDefault(name));

	public static double? ReadNumber(object? raw) => raw switch
	{
		double value => value,
		float value => value,
		int value => value,
		long value => value,
		decimal value => (double)value,
		bool value => value ? 1d : 0d,
		string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
			parsed,
		_ => null
	};

	public static int? ReadChannelIndex(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadChannelIndex(parameters.GetValueOrDefault(name));

	public static int? ReadChannelIndex(object? raw)
	{
		var value = ReadNumber(raw);
		return value is null || value < 0 ? null : (int)value;
	}

	public static string? ReadText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var raw = parameters.GetValueOrDefault(name);
		var text = raw as string ?? raw?.ToString();
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}

	public static SwitchMode ReadSwitchMode(IReadOnlyDictionary<string, object> parameters)
		=> ReadText(parameters, ModeParameter) switch
		{
			"on" => SwitchMode.On,
			"off" => SwitchMode.Off,
			_ => SwitchMode.Toggle
		};

	public static bool? Resolve(this SwitchMode mode, Func<bool?> currentValue) => mode switch
	{
		SwitchMode.On => true,
		SwitchMode.Off => false,
		_ => currentValue() is { } current ? !current : null
	};
}
