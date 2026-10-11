using System.Globalization;
using System.Text;
using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Integrations.Weather.Details;

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
		if (TwelveHour && string.IsNullOrEmpty(Culture.DateTimeFormat.AMDesignator))
		{
			return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
		}

		return time.ToString(Pattern(Culture.DateTimeFormat.ShortTimePattern, TwelveHour), Culture);
	}

	private static string Pattern(string culturePattern, bool twelveHour)
	{
		var builder = new StringBuilder(culturePattern.Length + 3);
		var quoted = false;
		var hasDesignator = false;
		foreach (var character in culturePattern)
		{
			if (character == '\'')
			{
				quoted = !quoted;
				builder.Append(character);
			}
			else if (quoted)
			{
				builder.Append(character);
			}
			else if (character is 'h' or 'H')
			{
				if (!twelveHour)
				{
					builder.Append('H');
				}
				else if (builder.Length == 0 || builder[^1] != 'h')
				{
					builder.Append('h');
				}
			}
			else if (character == 't')
			{
				hasDesignator = true;
				if (twelveHour)
				{
					builder.Append(character);
				}
			}
			else
			{
				builder.Append(character);
			}
		}

		var pattern = builder.ToString().Trim();

		return twelveHour && !hasDesignator ? pattern + " tt" : pattern;
	}

	private static string? Read(UiSurface surface, string key)
		=> surface.Attributes.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String &&
			value.GetString() is { Length: > 0 } text
				? text
				: null;

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
