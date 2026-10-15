using System.Globalization;
using System.Text;

namespace MacroDeck.Sdk.Locale;

/// <summary>
/// How the user wants a time of day written: the culture of the Macro Deck interface and the hour cycle
/// chosen in its settings, or the operating system's when the setting follows the system.
/// </summary>
/// <param name="Culture">The culture whose short time pattern and AM/PM designators are used.</param>
/// <param name="HourCycle">One of <see cref="HourCycles" />. Any other value formats as <see cref="HourCycles.H23" />.</param>
public sealed record TimeOfDayFormat(CultureInfo Culture, string HourCycle)
{
	/// <summary>
	/// Writes <paramref name="time" /> with the culture's short time pattern, switched to the hour cycle. A
	/// 24-hour cycle in a culture whose pattern is 12-hour has a two-digit hour, the way those languages write
	/// a 24-hour clock; a 24-hour pattern keeps its own. A 12-hour cycle in a culture without AM/PM designators
	/// is written in the invariant culture's <c>h:mm tt</c>.
	/// </summary>
	public string Format(TimeOnly time)
	{
		var twelveHour = HourCycle == HourCycles.H12;
		if (twelveHour && string.IsNullOrEmpty(Culture.DateTimeFormat.AMDesignator))
		{
			return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
		}

		return time.ToString(Pattern(Culture.DateTimeFormat.ShortTimePattern, twelveHour), Culture);
	}

	private static string Pattern(string culturePattern, bool twelveHour)
	{
		var builder = new StringBuilder(culturePattern.Length + 3);
		var quoted = false;
		var hasDesignator = false;
		var previous = '\0';
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
			else if (character == 'h' && !twelveHour)
			{
				if (previous != 'h')
				{
					builder.Append("HH");
				}
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

			previous = quoted ? '\0' : character;
		}

		var pattern = builder.ToString().Trim();

		return twelveHour && !hasDesignator ? pattern + " tt" : pattern;
	}
}
