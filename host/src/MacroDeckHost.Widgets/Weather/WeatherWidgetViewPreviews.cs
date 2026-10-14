using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Widgets.Weather;

internal static class WeatherWidgetViewPreviews
{
	[UiPreview("Clear day", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ClearDay()
	{
		return Build(WeatherPreviewSamples.ClearDay());
	}

	[UiPreview("Rain", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Rain()
	{
		return Build(WeatherPreviewSamples.Rain());
	}

	[UiPreview("Compact", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Compact()
	{
		return Build(WeatherPreviewSamples.ClearDay(),
			new WeatherWidgetData { Style = WeatherWidgetData.StyleCompact });
	}

	[UiPreview("Unavailable", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unavailable()
	{
		return Build(WeatherStatePayload.Unavailable(locationName: "Home"));
	}

	private static UiElement Build(WeatherStatePayload payload, WeatherWidgetData? config = null)
	{
		var icons = WeatherWidgetIcons.EnsureRegistered(WidgetPreviewResources.Store);
		var state = new UiAsyncState<WeatherStatePayload>(_ => Task.FromResult(payload), payload);

		return WeatherWidgetView.Build(state, config ?? new WeatherWidgetData(), icons);
	}
}
