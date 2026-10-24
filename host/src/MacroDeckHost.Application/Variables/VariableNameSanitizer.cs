using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public static class VariableNameSanitizer
{
	public const string PublicPrefix = "vars.";

	public static string Sanitize(string? input)
	{
		return VariableNames.Sanitize(input);
	}

	public static bool IsValid(string? name)
	{
		return VariableNames.IsValid(name);
	}

	public static string Canonicalize(string name)
	{
		return IsValid(name) ? name : Sanitize(name);
	}

	public static string ToPublicName(string name)
	{
		return PublicPrefix + name;
	}
}
