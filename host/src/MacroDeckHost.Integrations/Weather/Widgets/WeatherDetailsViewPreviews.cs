using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal static class WeatherDetailsViewPreviews
{
	[UiPreview("Clear day", Profile = UiPreviewProfiles.Widget)]
	public static UiElement ClearDay()
	{
		return Build(ClearDaySample());
	}

	[UiPreview("Unavailable", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unavailable()
	{
		return Build(WeatherDetailsState.Unavailable("Home"));
	}

	private static UiElement Build(WeatherDetailsState state)
	{
		return WeatherDetailsView.Build(new UiState<WeatherDetailsState>(state),
			new UiState<WeatherDetailsIcons>(WeatherDetailsIcons.None));
	}

	private static WeatherDetailsState ClearDaySample()
	{
		return new WeatherDetailsState
		{
			IsAvailable = true,
			LocationName = "Home",
			Temperature = 21.5,
			ApparentTemperature = 20.8,
			Condition = "clear",
			IsDay = true,
			Unit = "celsius",
			Days =
			[
				new WeatherDetailsDay { Date = "2026-08-26", Condition = "clear", Min = 14, Max = 23 },
				new WeatherDetailsDay { Date = "2026-08-27", Condition = "partly-cloudy", Min = 13, Max = 21 },
				new WeatherDetailsDay { Date = "2026-08-28", Condition = "rain", Min = 12, Max = 18 }
			],
			Hours =
			[
				new WeatherDetailsHour { Time = "12:00", Condition = "clear", Temperature = 21.5 },
				new WeatherDetailsHour { Time = "13:00", Condition = "clear", Temperature = 22.1 },
				new WeatherDetailsHour { Time = "14:00", Condition = "partly-cloudy", Temperature = 21.8 }
			],
			WindSpeed = 12,
			WindDirection = 220,
			Humidity = 48,
			Precipitation = 0,
			Sunrise = "06:12",
			Sunset = "20:47"
		};
	}
}
