using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

public sealed class DynamicOptionsResult
{
	public required IReadOnlyList<ActionParameterOption> Options { get; init; }

	public bool AllowsCustomValue { get; init; }

	public int? CacheSeconds { get; init; }

	/// <summary>
	/// Explains why <see cref="Options" /> could not be produced - a missing prerequisite, an ambiguous
	/// selection upstream, a resolution that failed. Without it an editor can only show an empty picker
	/// and leave the user guessing what to do first.
	/// <para>
	/// Default-empty means "no error": <see cref="Options" /> stands on its own, which is what every
	/// provider written before this existed keeps doing. An empty list is not an error by itself - a
	/// source that legitimately has nothing to offer right now should say so with an empty list and no
	/// error.
	/// </para>
	/// <para>
	/// A non-empty error <b>replaces</b> the option list in what the editor is shown, so return one only
	/// when there is nothing useful left to offer; anything in <see cref="Options" /> alongside it does
	/// not reach the user. Because the message is shown to the user, it must be localized.
	/// </para>
	/// </summary>
	public LocalizedText Error { get; init; }
}
