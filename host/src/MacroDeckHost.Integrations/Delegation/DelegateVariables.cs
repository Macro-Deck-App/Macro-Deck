using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Delegation;

internal static class DelegateVariables
{
	public const string Prefix = "delegate_";
	public const string Suffix = "_connected";

	public static VariableDefinition Declare(string variableKey, VariableConfiguration? configuration = null)
		=> VariableDefinition.Eager($"{Prefix}{variableKey}{Suffix}",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5)) with
			{
				DisplayName = MacroDeckStrings.Connection.Connected(),
				Configuration = configuration
			};

	public static bool Read(DelegateRemote remote) => remote.IsReachable && !remote.CredentialsRejected;

	public static string? KeyOfDefinitionId(string definitionId)
	{
		// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name
		// can never contain '-', so swapping the separator back recovers the name exactly.
		var variableName = definitionId.Replace('-', '_');
		if (!variableName.StartsWith(Prefix, StringComparison.Ordinal) ||
			!variableName.EndsWith(Suffix, StringComparison.Ordinal))
		{
			return null;
		}

		var key = variableName[Prefix.Length..^Suffix.Length];
		return key.Length > 0 ? key : null;
	}
}
