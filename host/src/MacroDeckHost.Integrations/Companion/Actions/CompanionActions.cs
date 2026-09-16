using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal static class CompanionActions
{
	internal const string BrightnessParameter = "brightness";
	internal const string OrientationParameter = "orientation";

	private const string Automatic = "automatic";
	private const string Portrait = "portrait";
	private const string Landscape = "landscape";

	public static IReadOnlyList<IActionDefinition> Create(CompanionTargetResolver resolver,
		Func<IVariableApi?> variables,
		Func<IUserVariableApi?> userVariables) =>
	[
		new CompanionAction("set-brightness",
			AppStrings.Integrations.Companion.Actions.SetBrightness.Name(),
			AppStrings.Integrations.Companion.Actions.SetBrightness.Description(),
			[
				ActionParameter.Slider(BrightnessParameter,
					min: 0,
					max: 100,
					label: AppStrings.Integrations.Companion.Variables.ScreenBrightnessPercent(),
					step: 1,
					defaultValue: 100)
			],
			resolver,
			parameters => ReadPercent(parameters.GetValueOrDefault(BrightnessParameter)) is { } percent
				? new CompanionCommand(CompanionCommand.SetBrightness, BrightnessPercent: percent)
				: null),
		new CompanionAction("set-orientation",
			AppStrings.Integrations.Companion.Actions.SetOrientation.Name(),
			AppStrings.Integrations.Companion.Actions.SetOrientation.Description(),
			[
				ActionParameter.Choice(OrientationParameter,
					options:
					[
						new ActionParameterOption
							{ Value = Automatic, Label = AppStrings.Integrations.Companion.Options.Automatic() },
						new ActionParameterOption
							{ Value = Portrait, Label = AppStrings.Integrations.Companion.Options.Portrait() },
						new ActionParameterOption
							{ Value = Landscape, Label = AppStrings.Integrations.Companion.Options.Landscape() }
					],
					label: AppStrings.Integrations.Companion.Variables.Orientation(),
					defaultValue: Automatic)
			],
			resolver,
			parameters =>
				parameters.GetValueOrDefault(OrientationParameter) is string orientation
					and (Automatic or Portrait or Landscape)
					? new CompanionCommand(CompanionCommand.SetOrientation, Orientation: orientation)
					: null),
		new CompanionAction("vibrate",
			AppStrings.Integrations.Companion.Actions.Vibrate.Name(),
			AppStrings.Integrations.Companion.Actions.Vibrate.Description(),
			[],
			resolver,
			_ => new CompanionCommand(CompanionCommand.Vibrate)),
		new CompanionAction("screen-on",
			AppStrings.Integrations.Companion.Actions.ScreenOn.Name(),
			AppStrings.Integrations.Companion.Actions.ScreenOn.Description(),
			[],
			resolver,
			_ => new CompanionCommand(CompanionCommand.ScreenOn),
			CompanionCapabilities.ScreenOn),
		new CompanionAction("screen-off",
			AppStrings.Integrations.Companion.Actions.ScreenOff.Name(),
			AppStrings.Integrations.Companion.Actions.ScreenOff.Description(),
			[],
			resolver,
			_ => new CompanionCommand(CompanionCommand.ScreenOff),
			CompanionCapabilities.ScreenOff),
		new CompanionAction("focus-host",
			AppStrings.Integrations.Companion.Actions.FocusHost.Name(),
			AppStrings.Integrations.Companion.Actions.FocusHost.Description(),
			[],
			resolver,
			_ => new CompanionCommand(CompanionCommand.Focus),
			CompanionCapabilities.Focus),
		new TakeScreenshotAction(resolver, variables, userVariables)
	];

	private static int? ReadPercent(object? raw)
	{
		double? value = raw switch
		{
			int i => i,
			long l => l,
			double d => d,
			float f => f,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => null
		};
		return value is >= 0d and <= 100d ? (int)Math.Round(value.Value) : null;
	}
}
