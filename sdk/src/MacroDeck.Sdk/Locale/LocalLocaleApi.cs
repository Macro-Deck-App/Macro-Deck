using System.Globalization;

namespace MacroDeck.Sdk.Locale;

/// <summary>
/// The <see cref="ILocaleApi" /> of a context that cannot ask Macro Deck: the process's current culture
/// with a <see cref="HourCycles.H23" /> clock.
/// </summary>
public sealed class LocalLocaleApi : ILocaleApi
{
	public static readonly LocalLocaleApi Instance = new();

	private LocalLocaleApi()
	{
	}

	public Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(new TimeOfDayFormat(CultureInfo.CurrentCulture, HourCycles.H23));
	}
}
