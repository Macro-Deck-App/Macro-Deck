namespace MacroDeck.Sdk.Colors;

/// <summary>Recognizes Color variable references in stored colour values.</summary>
public static class ColorReference
{
	/// <summary>
	/// Whether <paramref name="value" /> is a Color variable reference: the whole value is
	/// <c>{{ vars.name | color }}</c> followed by any number of Macro Deck's colour modifiers, within the
	/// length and step bounds Macro Deck resolves. Pass such a value to <see cref="IColorApi" /> rather than
	/// painting it.
	/// </summary>
	public static bool IsReference(string? value) => ColorGrammar.IsReference(value);
}
