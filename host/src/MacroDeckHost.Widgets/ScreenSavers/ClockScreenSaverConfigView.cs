using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal static class ClockScreenSaverConfigView
{
	public static UiElement Build(JsonElement stored)
	{
		var config = ClockScreenSaverData.Parse(stored);
		var hourCycle = new UiState<string>(config.HourCycle);
		var showDate = new UiState<bool>(config.ShowDate);
		var showSeconds = new UiState<bool>(config.ShowSeconds);

		return new UiConfigStack
		{
			Key = "root",
			Children =
			[
				new UiChoiceInput
				{
					Key = "hourCycle",
					Label = AppStrings.Widgets.Clock.HourCycle(),
					Binding = Bind.To(hourCycle),
					Options = UiValue.Of<IReadOnlyList<UiOption>>([
						UiOption.Of("auto", AppStrings.Widgets.Clock.HourCycleAuto()),
						UiOption.Of("12h", "12"),
						UiOption.Of("24h", "24"),
					]),
				},
				new UiBooleanInput
				{
					Key = "showDate",
					Label = AppStrings.Widgets.Clock.Date(),
					Binding = Bind.To(showDate),
				},
				new UiBooleanInput
				{
					Key = "showSeconds",
					Label = AppStrings.Widgets.Clock.Seconds(),
					Binding = Bind.To(showSeconds),
				},
			],
		};
	}
}
