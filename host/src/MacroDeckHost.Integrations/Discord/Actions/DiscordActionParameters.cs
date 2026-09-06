using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal static class DiscordActionParameters
{
	public const string StateParameter = "action";

	public const string ModeParameter = "mode";

	public const string StateOn = "on";
	public const string StateOff = "off";
	public const string StateToggle = "toggle";

	public const string ModeSet = "set";
	public const string ModeIncrease = "increase";
	public const string ModeDecrease = "decrease";

	public static ActionParameter StateSelector(LocalizedText onLabel, LocalizedText offLabel)
		=> ActionParameter.Choice(StateParameter,
			options:
			[
				new ActionParameterOption
					{ Value = StateToggle, Label = AppStrings.Integrations.Discord.Actions.VoiceToggle.ToggleOption() },
				new ActionParameterOption { Value = StateOn, Label = onLabel },
				new ActionParameterOption { Value = StateOff, Label = offLabel }
			],
			label: AppStrings.Integrations.Discord.Actions.VoiceToggle.ActionLabel(),
			defaultValue: StateToggle);

	public static ActionParameter VolumeModeSelector()
		=> ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = ModeSet, Label = AppStrings.Integrations.Discord.Actions.SetVoiceVolume.SetToOption() },
				new ActionParameterOption
				{
					Value = ModeIncrease,
					Label = AppStrings.Integrations.Discord.Actions.SetVoiceVolume.IncreaseByOption()
				},
				new ActionParameterOption
				{
					Value = ModeDecrease,
					Label = AppStrings.Integrations.Discord.Actions.SetVoiceVolume.DecreaseByOption()
				}
			],
			label: AppStrings.Integrations.Discord.Actions.SetVoiceVolume.ModeLabel(),
			defaultValue: ModeSet);

	public static bool ResolveState(object? raw, bool current)
		=> (raw as string) switch
		{
			StateOn => true,
			StateOff => false,
			_ => !current
		};

	public static string ReadMode(object? raw) => raw as string ?? ModeSet;

	public static double ReadNumber(object? raw, double fallback = 0d) => raw switch
	{
		double d => d,
		float f => f,
		int i => i,
		long l => l,
		decimal m => (double)m,
		string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
		_ => fallback
	};

	public static bool ReadBool(object? raw) => raw switch
	{
		bool b => b,
		string s => bool.TryParse(s, out var parsed) && parsed,
		_ => false
	};

	public static string? ReadText(object? raw)
	{
		var text = raw as string;
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}
}
