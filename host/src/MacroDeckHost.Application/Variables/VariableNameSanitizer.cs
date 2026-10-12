using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public static class VariableNameSanitizer
{
	public const string PublicPrefix = "vars.";

	public static string Sanitize(string? input) => VariableNames.Sanitize(input);

	public static bool IsValid(string? name) => VariableNames.IsValid(name);

	public static string ToPublicName(string name) => PublicPrefix + name;
}
