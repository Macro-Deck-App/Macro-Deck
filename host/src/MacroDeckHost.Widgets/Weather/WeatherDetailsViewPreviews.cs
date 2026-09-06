using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Widgets.Weather;

internal static class WeatherDetailsViewPreviews
{
	[UiPreview("Clear day", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ClearDay() => Build(WeatherPreviewSamples.ClearDay());

	[UiPreview("Unavailable", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unavailable() => Build(WeatherStatePayload.Unavailable(locationName: "Home"));

	private static UiElement Build(WeatherStatePayload payload)
	{
		var icons = WeatherWidgetIcons.EnsureRegistered(WidgetPreviewResources.Store);
		var state = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(payload), payload);

		return WeatherDetailsView.Build(state, icons);
	}
}
