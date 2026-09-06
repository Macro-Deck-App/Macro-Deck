using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.HistoryGraph;

internal static class HistoryGraphWidgetViewPreviews
{
	[UiPreview("Default", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Default()
		=> HistoryGraphWidgetView.Build(new UiState<HistoryGraphViewState>(new HistoryGraphViewState
			{
				Value = "21.4",
				Unit = "°C",
				Subtitle = "Living Room",
				Points = [0.2, 0.3, 0.25, 0.4, 0.55, 0.5, 0.6, 0.7, 0.65, 0.8],
			}),
			new HistoryGraphWidgetData { Title = "Temperature" });

	[UiPreview("Empty", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Empty()
		=> HistoryGraphWidgetView.Build(new UiState<HistoryGraphViewState>(HistoryGraphViewState.Empty),
			new HistoryGraphWidgetData { Title = "Temperature" });
}
