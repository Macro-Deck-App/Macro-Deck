using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal static class MouseParameters
{
	public static ActionParameter Button { get; } = ActionParameter.Choice("button",
		options:
		[
			new ActionParameterOption
				{ Value = "left", Label = AppStrings.Integrations.Mouse.Actions.Shared.ButtonOptionLeft() },
			new ActionParameterOption
				{ Value = "right", Label = AppStrings.Integrations.Mouse.Actions.Shared.ButtonOptionRight() },
			new ActionParameterOption
				{ Value = "middle", Label = AppStrings.Integrations.Mouse.Actions.Shared.ButtonOptionMiddle() },
			new ActionParameterOption
				{ Value = "back", Label = AppStrings.Integrations.Mouse.Actions.Shared.ButtonOptionBack() },
			new ActionParameterOption
				{ Value = "forward", Label = AppStrings.Integrations.Mouse.Actions.Shared.ButtonOptionForward() }
		],
		label: AppStrings.Integrations.Mouse.Actions.Shared.ButtonLabel(),
		description: AppStrings.Integrations.Mouse.Actions.Shared.ButtonDescription(),
		defaultValue: "left");

	public static IReadOnlyList<ActionParameter> OptionalPosition { get; } =
	[
		ActionParameter.Choice("positionMode",
			options:
			[
				new ActionParameterOption
					{ Value = "current", Label = AppStrings.Integrations.Mouse.Actions.Shared.PositionCurrent() },
				new ActionParameterOption
					{ Value = "absolute", Label = AppStrings.Integrations.Mouse.Actions.Shared.PositionAbsolute() },
				new ActionParameterOption
					{ Value = "relative", Label = AppStrings.Integrations.Mouse.Actions.Shared.PositionRelative() }
			],
			label: AppStrings.Integrations.Mouse.Actions.Shared.PositionLabel(),
			description: AppStrings.Integrations.Mouse.Actions.Shared.PositionDescription(),
			defaultValue: "current"),
		X("x",
				AppStrings.Integrations.Mouse.Actions.Shared.XLabel(),
				AppStrings.Integrations.Mouse.Actions.Shared.XDescription())
			.OnlyWhen("positionMode", "absolute", "relative"),
		Y("y",
				AppStrings.Integrations.Mouse.Actions.Shared.YLabel(),
				AppStrings.Integrations.Mouse.Actions.Shared.YDescription())
			.OnlyWhen("positionMode", "absolute", "relative")
	];

	public static ActionParameter X(string name, LocalizedText label, LocalizedText description)
		=> ActionParameter.Number(name, label: label, description: description, step: 1, defaultValue: 0);

	public static ActionParameter Y(string name, LocalizedText label, LocalizedText description)
		=> ActionParameter.Number(name, label: label, description: description, step: 1, defaultValue: 0);
}
