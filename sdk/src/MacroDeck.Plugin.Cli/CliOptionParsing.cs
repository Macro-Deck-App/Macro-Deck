using System.CommandLine.Parsing;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// A small, explicit token-to-value parser shared by every closed-vocabulary option
/// (<c>--verbosity</c>, <c>--output</c>, <c>--mode</c>, <c>--report</c>) instead of
/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />: a mapping states the exact command-line
/// spelling next to the value it means, which is what lets <c>self-registering</c> (a token
/// <see cref="Enum" /> parsing alone could never produce from a C# member name) read the same way every
/// other option does.
/// </summary>
internal static class CliOptionParsing
{
	public static T ParseToken<T>(ArgumentResult result, T defaultValue, params (string Token, T Value)[] mapping)
	{
		if (result.Tokens.Count == 0)
		{
			return defaultValue;
		}

		var raw = result.Tokens[0].Value;

		foreach (var (token, value) in mapping)
		{
			if (string.Equals(token, raw, StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}

		result.AddError(
			$"'{raw}' is not recognized. Expected one of: {string.Join(", ", mapping.Select(m => m.Token))}.");
		return defaultValue;
	}
}
