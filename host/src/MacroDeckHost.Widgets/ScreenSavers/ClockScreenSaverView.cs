using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Widgets.Clock;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal static class ClockScreenSaverView
{
	public static UiElement Build(ClockScreenSaverData config, UiState<int> position)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(position);

		return new UiStack
		{
			Key = "screensaver",
			Direction = UiComponentDirections.Vertical,
			Justify = ScreenSaverDrift.Justify(position),
			Align = ScreenSaverDrift.Align(position),
			Padding = 0.06,
			Children = [ClockWidgetView.Build(config.ToClock(), 0)],
		};
	}
}
