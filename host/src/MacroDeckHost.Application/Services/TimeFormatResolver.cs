using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Services;

public interface ISystemHourCycleReader
{
	string? Read();
}

public static class HourCycles
{
	public const string H12 = "h12";
	public const string H23 = "h23";

	public static string? FromPattern(string? pattern)
	{
		if (string.IsNullOrEmpty(pattern))
		{
			return null;
		}

		var unquoted = StripQuoted(pattern);
		if (unquoted.AsSpan().IndexOfAny('H', 'k') >= 0)
		{
			return H23;
		}

		return unquoted.AsSpan().IndexOfAny('h', 'K') >= 0 ? H12 : null;
	}

	private static string StripQuoted(string pattern)
	{
		var builder = new StringBuilder(pattern.Length);
		var quoted = false;
		foreach (var character in pattern)
		{
			if (character == '\'')
			{
				quoted = !quoted;
			}
			else if (!quoted)
			{
				builder.Append(character);
			}
		}

		return builder.ToString();
	}
}

public sealed record TimeOfDayFormat(CultureInfo Culture, string HourCycle)
{
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
}

public sealed class TimeFormatResolver
{
	private readonly IAppPreferenceService _preferences;
	private readonly ISystemHourCycleReader _systemReader;

	public TimeFormatResolver(IAppPreferenceService preferences, ISystemHourCycleReader systemReader)
	{
		_preferences = preferences;
		_systemReader = systemReader;
	}

	public static async Task<TimeOfDayFormat> ResolveAsync(IServiceScopeFactory scopeFactory)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<TimeFormatResolver>().ResolveAsync();
	}

	public async Task<TimeOfDayFormat> ResolveAsync()
	{
		var culture = CultureInfo.GetCultureInfo((await _preferences.GetLocalization()).Culture);
		var hourCycle = await _preferences.GetTimeFormat() switch
		{
			AppPreferenceService.TimeFormat12h => HourCycles.H12,
			AppPreferenceService.TimeFormat24h => HourCycles.H23,
			_ => _systemReader.Read() ??
				HourCycles.FromPattern(culture.DateTimeFormat.ShortTimePattern) ??
				HourCycles.H23
		};

		return new TimeOfDayFormat(culture, hourCycle);
	}

	public async Task<string> ResolveHourCycle() => (await ResolveAsync()).HourCycle;
}
