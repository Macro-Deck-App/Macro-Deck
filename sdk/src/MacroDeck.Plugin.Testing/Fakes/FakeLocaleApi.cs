using System.Globalization;
using MacroDeck.Sdk.Locale;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// <see cref="ILocaleApi" /> answering <see cref="TimeOfDayFormat" />, which defaults to the invariant culture
/// with a 24-hour clock. Set it to the user preference a test needs.
/// </summary>
public sealed class FakeLocaleApi : ILocaleApi
{
	/// <summary>The format every call answers.</summary>
	public TimeOfDayFormat TimeOfDayFormat { get; set; } = new(CultureInfo.InvariantCulture, HourCycles.H23);

	/// <inheritdoc />
	public Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(TimeOfDayFormat);
	}
}
