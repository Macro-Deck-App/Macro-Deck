using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal static class NowPlayingScreenSaverView
{
	public static UiElement Build(
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerIconResources icons,
		ClockScreenSaverData clock,
		UiState<int> clockPosition)
	{
		ArgumentNullException.ThrowIfNull(state);

		return new UiStack
		{
			Key = "nowPlaying",
			Direction = UiComponentDirections.Vertical,
			Justify = ScreenSaverDrift.Justify(clockPosition),
			Align = ScreenSaverDrift.Align(clockPosition),
			Padding = 0.06,
			Children =
			[
				new UiWhen
				{
					Key = "playingGate",
					Condition = () => state.Value.HasTrack,
					Content = () => MusicPlayerWidgetView.Build(state, config, icons, 0),
				},
				new UiWhen
				{
					Key = "idleGate",
					Condition = () => !state.Value.HasTrack,
					Content = () => ClockScreenSaverView.Build(clock, clockPosition),
				},
			],
		};
	}
}
