using System.Globalization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldActionParameters
{
	public const string Scene = "scene";
	public const string Layer = "layer";
	public const string Effect = "effect";
	public const string Track = "track";
	public const string Mode = "mode";
	public const string Volume = "volume";
	public const string Amount = "amount";
	public const string Orientation = "orientation";
	public const string Variable = "variable";

	public const string ModeToggle = "toggle";

	public const string OrientationHorizontal = "horizontal";
	public const string OrientationVertical = "vertical";

	public static readonly IReadOnlyList<ActionParameterOption> LayerVisibilityModes =
	[
		new()
			{ Value = ModeToggle, Label = AppStrings.Integrations.Meld.Options.Toggle() },
		new()
			{ Value = "show", Label = AppStrings.Integrations.Meld.Options.Show() },
		new()
			{ Value = "hide", Label = AppStrings.Integrations.Meld.Options.Hide() }
	];

	public static readonly IReadOnlyList<ActionParameterOption> EffectStateModes =
	[
		new()
			{ Value = ModeToggle, Label = AppStrings.Integrations.Meld.Options.Toggle() },
		new()
			{ Value = "enable", Label = AppStrings.Integrations.Meld.Options.Enable() },
		new()
			{ Value = "disable", Label = AppStrings.Integrations.Meld.Options.Disable() }
	];

	public static readonly IReadOnlyList<ActionParameterOption> TrackMuteModes =
	[
		new()
			{ Value = ModeToggle, Label = AppStrings.Integrations.Meld.Options.Toggle() },
		new()
			{ Value = "mute", Label = AppStrings.Integrations.Meld.Options.Mute() },
		new()
			{ Value = "unmute", Label = AppStrings.Integrations.Meld.Options.Unmute() }
	];

	public static readonly IReadOnlyList<ActionParameterOption> TrackMonitoringModes =
	[
		new()
			{ Value = ModeToggle, Label = AppStrings.Integrations.Meld.Options.Toggle() },
		new()
			{ Value = "on", Label = AppStrings.Integrations.Meld.Options.On() },
		new()
			{ Value = "off", Label = AppStrings.Integrations.Meld.Options.Off() }
	];

	public static readonly IReadOnlyList<ActionParameterOption> ScreenshotOrientations =
	[
		new()
			{ Value = OrientationHorizontal, Label = AppStrings.Integrations.Meld.Options.Horizontal() },
		new()
			{ Value = OrientationVertical, Label = AppStrings.Integrations.Meld.Options.Vertical() }
	];

	public static double? ReadNumber(object? raw) => raw switch
	{
		double d => d,
		float f => f,
		int i => i,
		long l => l,
		string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
		_ => null
	};
}
