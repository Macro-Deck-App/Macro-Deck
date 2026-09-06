using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Variables;

public static class VariableNameSanitizer
{
	public const string PublicPrefix = "vars.";

	private static readonly Regex _validPattern = new("^[a-z][a-z0-9_]*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public static string Sanitize(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		var trimmed = input.Trim();
		if (trimmed.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed[PublicPrefix.Length..];
		}

		var folded = trimmed.Normalize(NormalizationForm.FormD);
		var sb = new StringBuilder(folded.Length);
		foreach (var ch in folded)
		{
			var category = CharUnicodeInfo.GetUnicodeCategory(ch);
			if (category == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
			{
				sb.Append(ch);
			}
			else if (ch is >= 'A' and <= 'Z')
			{
				sb.Append(char.ToLowerInvariant(ch));
			}
			else
			{
				sb.Append('_');
			}
		}

		var collapsed = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');

		if (collapsed.Length > 0 && char.IsDigit(collapsed[0]))
		{
			collapsed = "v_" + collapsed;
		}

		return collapsed;
	}

	public static bool IsValid(string? name) => !string.IsNullOrEmpty(name) && _validPattern.IsMatch(name);

	public static string ToPublicName(string name) => PublicPrefix + name;
}
