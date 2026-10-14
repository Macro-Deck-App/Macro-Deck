using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Integrations;

internal static class ConfigurationIdentityAllocator
{
	internal const int MaximumAttempts = 32;

	public static string? Allocate(
		string title,
		IReadOnlySet<string> occupiedKeys,
		string? keyThatMustChange = null,
		Func<int>? suffixFactory = null)
	{
		var sanitized = Sanitize(title);
		var suffix = suffixFactory ?? (() => RandomNumberGenerator.GetInt32(1000, 10000));

		for (var attempt = 0; attempt < MaximumAttempts; attempt++)
		{
			var digits = suffix().ToString(CultureInfo.InvariantCulture);
			var candidate = sanitized.Length == 0
				? $"configuration_{digits}"
				: attempt == 0
				&& !occupiedKeys.Contains(sanitized)
				&& !string.Equals(sanitized, keyThatMustChange, StringComparison.Ordinal)
					? sanitized
					: $"{sanitized}_{digits}";

			if (!occupiedKeys.Contains(candidate)
				&& !string.Equals(candidate, keyThatMustChange, StringComparison.Ordinal))
			{
				return candidate;
			}
		}

		return null;
	}

	internal static string Sanitize(string title)
	{
		var normalized = title.Normalize(NormalizationForm.FormD);
		var result = new StringBuilder(normalized.Length);
		var separator = false;
		foreach (var character in normalized)
		{
			var category = CharUnicodeInfo.GetUnicodeCategory(character);
			if (category == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
			{
				if (separator && result.Length > 0)
				{
					result.Append('_');
				}

				result.Append(char.ToLowerInvariant(character));
				separator = false;
			}
			else
			{
				separator = true;
			}
		}

		return result.ToString().Trim('_');
	}
}
