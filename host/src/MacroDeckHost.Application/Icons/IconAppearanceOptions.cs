using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Icons;

public static class IconAppearanceOptions
{
	public static IReadOnlyList<ActionParameterOption> BuiltIn()
	{
		return
		[
			new ActionParameterOption
			{
				Value = WidgetAppearanceValues.Reset, Label = AppStrings.Widgets.Appearance.Icon.AppearanceAutomatic()
			},
			new ActionParameterOption
				{ Value = WidgetIconReference.DefaultAppearance, Label = AppStrings.IconPacks.Appearances.Default() },
			new ActionParameterOption
				{ Value = "colorScheme=light", Label = AppStrings.IconPacks.Appearances.Kind.Light() },
			new ActionParameterOption
				{ Value = "colorScheme=dark", Label = AppStrings.IconPacks.Appearances.Kind.Dark() },
			new ActionParameterOption
				{ Value = "motion=static", Label = AppStrings.IconPacks.Appearances.Kind.Static() },
			new ActionParameterOption
				{ Value = "motion=animated", Label = AppStrings.IconPacks.Appearances.Kind.Animated() },
			new ActionParameterOption
			{
				Value = "colorScheme=light;motion=static", Label = AppStrings.IconPacks.Appearances.Kind.LightStatic()
			},
			new ActionParameterOption
			{
				Value = "colorScheme=light;motion=animated",
				Label = AppStrings.IconPacks.Appearances.Kind.LightAnimated()
			},
			new ActionParameterOption
			{
				Value = "colorScheme=dark;motion=static", Label = AppStrings.IconPacks.Appearances.Kind.DarkStatic()
			},
			new ActionParameterOption
			{
				Value = "colorScheme=dark;motion=animated", Label = AppStrings.IconPacks.Appearances.Kind.DarkAnimated()
			}
		];
	}
}
