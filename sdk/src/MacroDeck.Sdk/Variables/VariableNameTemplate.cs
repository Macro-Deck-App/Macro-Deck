namespace MacroDeck.Sdk.Variables;

/// <summary>
/// The shared placeholder convention for a <see cref="IVariableProvider.DeclaredVariables"/> template
/// name. <c>&lt;</c> and <c>&gt;</c> cannot occur in a canonical variable name, so a template can never
/// be mistaken for, or accidentally registered as, a real variable.
/// </summary>
public static class VariableNameTemplate
{
	public const string PlaceholderStart = "<";

	public const string PlaceholderEnd = ">";

	public static string Placeholder(string label) => $"{PlaceholderStart}{label}{PlaceholderEnd}";

	/// <summary>Whether a declared name is a template rather than a real variable name.</summary>
	public static bool IsTemplate(string? name)
		=> name is not null && name.Contains(PlaceholderStart, StringComparison.Ordinal);
}
