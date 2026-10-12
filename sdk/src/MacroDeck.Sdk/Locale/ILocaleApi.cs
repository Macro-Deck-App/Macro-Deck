namespace MacroDeck.Sdk.Locale;

/// <summary>The user's language and clock preferences, for text an integration formats itself.</summary>
public interface ILocaleApi
{
	/// <summary>
	/// The time format the user currently wants. Ask again for each piece of text: the user can change
	/// the language, the time format setting or the operating system clock at any time.
	/// </summary>
	Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default);
}
