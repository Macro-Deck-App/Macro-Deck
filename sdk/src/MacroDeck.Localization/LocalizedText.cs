using System.Text.Json.Serialization;
using MacroDeck.Localization.Serialization;

namespace MacroDeck.Localization;

/// <summary>
/// Text on a user-facing contract: either a literal already in its final form - a device name, a value
/// the user typed - or a <see cref="LocalizedString" /> to be resolved in the reader's culture. Both
/// implicitly convert, so <c>Label = "Client ID"</c> and <c>Label = MyStrings.ClientId()</c> both compile.
/// </summary>
/// <remarks>
/// Serializes as a bare JSON string when literal and as
/// <c>{"$localized":{"scope":…,"key":…,"arguments":{…}}}</c> when localized. Both shapes are legal on
/// the same property, which is what lets a producer built before this existed keep emitting plain
/// strings: a reader has to handle either, and the string case is byte-identical to what it always was.
/// </remarks>
[JsonConverter(typeof(LocalizedTextJsonConverter))]
public readonly record struct LocalizedText
{
	private LocalizedText(string? literal, LocalizedString? localized)
	{
		Literal = literal;
		Localized = localized;
	}

	/// <summary>The literal text, or <c>null</c> when this is a localized reference or absent.</summary>
	public string? Literal { get; }

	/// <summary>The localized reference, or <c>null</c> when this is literal text or absent.</summary>
	public LocalizedString? Localized { get; }

	/// <summary>Whether this carries a localized reference rather than literal text.</summary>
	public bool IsLocalized => Localized.HasValue;

	/// <summary>Whether this carries nothing at all - the <c>default</c> value.</summary>
	public bool IsEmpty => Literal is null && Localized is null;

	/// <summary>Wraps literal text that is already in its final form.</summary>
	public static LocalizedText FromLiteral(string? literal)
		=> literal is null ? default : new LocalizedText(literal, null);

	/// <summary>Wraps a reference to be resolved in the reader's culture.</summary>
	public static LocalizedText FromLocalized(LocalizedString localized) => new(null, localized);

	/// <summary>Converts literal text.</summary>
	public static implicit operator LocalizedText(string? literal) => FromLiteral(literal);

	/// <summary>Converts a localized reference.</summary>
	public static implicit operator LocalizedText(LocalizedString localized) => FromLocalized(localized);

	/// <summary>Renders the literal, or the key - for diagnostics only, never for display.</summary>
	public override string ToString() => Literal ?? Localized?.ToString() ?? string.Empty;
}
