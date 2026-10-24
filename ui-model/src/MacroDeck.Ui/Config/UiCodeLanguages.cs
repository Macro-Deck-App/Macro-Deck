namespace MacroDeck.Ui.Config;

/// <summary>
/// The <see cref="UiCodeInput.Language" /> values Macro Deck's code editor ships highlighting for. A code
/// parameter's <c>Language</c> takes the same values.
///
/// <para>
/// <b>Open, deliberately.</b> A language a client does not know is edited as plain text rather than
/// refused, so a provider may name a language a newer client highlights without breaking an older one. For
/// that reason there is no <c>IsKnown</c>, and the list is named <see cref="WellKnown" />, not <c>All</c>.
/// </para>
/// </summary>
public static class UiCodeLanguages
{
	/// <summary>Plain text, without highlighting.</summary>
	public const string Text = "text";

	/// <summary>JSON. The editor also reports a document that does not parse.</summary>
	public const string Json = "json";

	/// <summary>Liquid templates, the syntax Macro Deck's own templates use.</summary>
	public const string Liquid = "liquid";

	/// <summary>AutoHotkey v2 scripts.</summary>
	public const string AutoHotkey = "autohotkey";

	/// <summary>The languages this package ships names for, in declaration order. Not exhaustive - see the
	/// type's remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Text, Json, Liquid, AutoHotkey];
}
