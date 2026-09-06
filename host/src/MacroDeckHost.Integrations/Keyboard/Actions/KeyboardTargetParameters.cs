using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal static class KeyboardTargetParameters
{
	public static readonly IReadOnlyList<ActionParameter> All =
	[
		ActionParameter.Autocomplete("targetProcess",
			label: AppStrings.Integrations.Keyboard.Config.TargetProcessLabel(),
			description: AppStrings.Integrations.Keyboard.Config.TargetProcessDescription(),
			optionsSourceId: "system.processes",
			placeholder: AppStrings.Integrations.Keyboard.Config.TargetProcessPlaceholder()),
		ActionParameter.Choice("targetMode",
			options:
			[
				new ActionParameterOption
				{
					Value = "focused",
					Label = AppStrings.Integrations.Keyboard.Config.TargetModeFocusedOption()
				},
				new ActionParameterOption
				{
					Value = "focus-send",
					Label = AppStrings.Integrations.Keyboard.Config.TargetModeFocusSendOption()
				},
				new ActionParameterOption
				{
					Value = "background",
					Label = AppStrings.Integrations.Keyboard.Config.TargetModeBackgroundOption()
				}
			],
			label: AppStrings.Integrations.Keyboard.Config.TargetModeLabel(),
			description: AppStrings.Integrations.Keyboard.Config.TargetModeDescription(),
			defaultValue: "focused")
	];
}
