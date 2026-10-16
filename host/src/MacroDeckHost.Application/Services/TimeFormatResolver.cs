using System.Globalization;
using System.Text;
using MacroDeck.Sdk.Locale;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Services;

public interface ISystemHourCycleReader
{
	string? Read();
}

public sealed class SystemTimePatternHourCycleReader(Func<string?> readPattern) : ISystemHourCycleReader
{
	public string? Read()
	{
		return HourCyclePatterns.FromPattern(readPattern());
	}
}

public sealed class HostLocaleApi(IServiceScopeFactory scopeFactory) : ILocaleApi
{
	public Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default)
	{
		return TimeFormatResolver.ResolveAsync(scopeFactory);
	}
}

public static class HourCyclePatterns
{
	public static string? FromPattern(string? pattern)
	{
		if (string.IsNullOrEmpty(pattern))
		{
			return null;
		}

		var unquoted = StripQuoted(pattern);
		if (unquoted.AsSpan().IndexOfAny('H', 'k') >= 0)
		{
			return HourCycles.H23;
		}

		return unquoted.AsSpan().IndexOfAny('h', 'K') >= 0 ? HourCycles.H12 : null;
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

		return new TimeOfDayFormat(culture,
			ResolveHourCycle(await _preferences.GetTimeFormat(), culture, _systemReader));
	}

	public static string ResolveHourCycle(string timeFormat, CultureInfo culture, ISystemHourCycleReader systemReader)
	{
		return timeFormat switch
		{
			AppPreferenceService.TimeFormat12H => HourCycles.H12,
			AppPreferenceService.TimeFormat24H => HourCycles.H23,
			_ => systemReader.Read()
				?? HourCyclePatterns.FromPattern(culture.DateTimeFormat.ShortTimePattern) ?? HourCycles.H23
		};
	}

	public static bool WritesPaddedHour(TimeOfDayFormat format)
	{
		return format.Format(new TimeOnly(1, 0)).Contains("01", StringComparison.Ordinal);
	}
}
