using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Icons;

public static class IconAppearanceOptions
{
	public static IReadOnlyList<ActionParameterOption> BuiltIn()
		=>
		[
			new() { Value = WidgetAppearanceValues.Reset, Label = AppStrings.Widgets.Appearance.Icon.AppearanceAutomatic() },
			new() { Value = WidgetIconReference.DefaultAppearance, Label = AppStrings.IconPacks.Appearances.Default() },
			new() { Value = "colorScheme=light", Label = AppStrings.IconPacks.Appearances.Kind.Light() },
			new() { Value = "colorScheme=dark", Label = AppStrings.IconPacks.Appearances.Kind.Dark() },
			new() { Value = "motion=static", Label = AppStrings.IconPacks.Appearances.Kind.Static() },
			new() { Value = "motion=animated", Label = AppStrings.IconPacks.Appearances.Kind.Animated() },
			new() { Value = "colorScheme=light;motion=static", Label = AppStrings.IconPacks.Appearances.Kind.LightStatic() },
			new() { Value = "colorScheme=light;motion=animated", Label = AppStrings.IconPacks.Appearances.Kind.LightAnimated() },
			new() { Value = "colorScheme=dark;motion=static", Label = AppStrings.IconPacks.Appearances.Kind.DarkStatic() },
			new() { Value = "colorScheme=dark;motion=animated", Label = AppStrings.IconPacks.Appearances.Kind.DarkAnimated() }
		];
}
