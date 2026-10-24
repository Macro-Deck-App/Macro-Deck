using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public static class DeclaredVariableNames
{
	public static string PublishedNameOf(VariableDefinition declared)
	{
		var name = declared.Name ?? declared.ResolvedId ?? string.Empty;
		return VariableNameTemplate.IsTemplate(name) ? name : VariableNameSanitizer.Canonicalize(name);
	}
}
