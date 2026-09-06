using System.Text.RegularExpressions;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// Redacts error detail dictionaries before they leave the host or the plugin process, so a stray
/// diagnostic can never leak a secret onto the wire or into a log.
/// </summary>
public static partial class ProtocolDiagnostics
{
	private const string RedactedValue = "***REDACTED***";

	[GeneratedRegex(@"secret|token|password|authorization|api[-_]?key|credential",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex SensitiveKeyPattern();

	/// <summary>Masks values whose key looks sensitive and truncates the result to
	/// <see cref="ProtocolLimits.MaxErrorDetailEntries" />.</summary>
	public static IReadOnlyDictionary<string, string> Redact(IReadOnlyDictionary<string, string>? details)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);

		if (details is null)
		{
			return result;
		}

		foreach (var pair in details)
		{
			if (result.Count >= ProtocolLimits.MaxErrorDetailEntries)
			{
				break;
			}

			result[pair.Key] = SensitiveKeyPattern().IsMatch(pair.Key) ? RedactedValue : pair.Value;
		}

		return result;
	}
}
