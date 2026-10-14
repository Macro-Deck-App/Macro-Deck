using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal static class KeyboardTargetParameters
{
	private static readonly ActionParameter _targetProcess = ActionParameter.Autocomplete("targetProcess",
		AppStrings.Integrations.Keyboard.Config.TargetProcessLabel(),
		AppStrings.Integrations.Keyboard.Config.TargetProcessDescription(),
		optionsSourceId: "system.processes",
		placeholder: AppStrings.Integrations.Keyboard.Config.TargetProcessPlaceholder());

	public static readonly IReadOnlyList<ActionParameter> All =
	[
		_targetProcess,
		ActionParameter.Choice("targetMode",
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
			AppStrings.Integrations.Keyboard.Config.TargetModeLabel(),
			AppStrings.Integrations.Keyboard.Config.TargetModeDescription(),
			"focused")
	];

	public static readonly IReadOnlyList<ActionParameter> Hold =
	[
		_targetProcess,
		ActionParameter.Choice("targetMode",
			[
				new ActionParameterOption
				{
					Value = "focused",
					Label = AppStrings.Integrations.Keyboard.Config.TargetModeFocusedOption()
				},
				new ActionParameterOption
				{
					Value = "background",
					Label = AppStrings.Integrations.Keyboard.Config.TargetModeBackgroundOption()
				}
			],
			AppStrings.Integrations.Keyboard.Config.TargetModeLabel(),
			AppStrings.Integrations.Keyboard.Config.HoldTargetModeDescription(),
			"focused")
	];
}
