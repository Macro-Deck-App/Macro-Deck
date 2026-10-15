using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Locale;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal sealed record WeatherDetailsTimeFormat(CultureInfo Culture, bool TwelveHour)
{
	private const string TwelveHourCycle = "h12";

	public static WeatherDetailsTimeFormat? From(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		var culture = Read(surface, UiDialogSurfaceAttributes.Culture) is { } name && TryCulture(name, out var found)
			? found
			: CultureInfo.CurrentCulture;

		return Read(surface, UiDialogSurfaceAttributes.HourCycle) is { } hourCycle
			? new WeatherDetailsTimeFormat(culture, string.Equals(hourCycle, TwelveHourCycle, StringComparison.Ordinal))
			: null;
	}

	public string Format(TimeOnly time)
	{
		return new TimeOfDayFormat(Culture, TwelveHour ? HourCycles.H12 : HourCycles.H23).Format(time);
	}

	private static string? Read(UiSurface surface, string key)
	{
		return surface.Attributes.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.String
			&& value.GetString() is { Length: > 0 } text
				? text
				: null;
	}

	private static bool TryCulture(string name, out CultureInfo culture)
	{
		try
		{
			culture = CultureInfo.GetCultureInfo(name);
			return true;
		}
		catch (CultureNotFoundException)
		{
			culture = CultureInfo.CurrentCulture;
			return false;
		}
	}
}
