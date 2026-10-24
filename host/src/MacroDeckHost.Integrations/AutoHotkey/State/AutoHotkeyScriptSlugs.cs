using System.Globalization;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.AutoHotkey.State;

internal static class AutoHotkeyScriptSlugs
{
	// The running variable id is autohotkey-script-<slug>-running and a declared id holds 64 characters.
	public const int MaxLength = 38;

	public static string Create(string name, Guid id, IEnumerable<string> taken)
	{
		var slug = Shorten(VariableNames.Sanitize(name), MaxLength);
		if (slug.Length == 0)
		{
			slug = "s" + id.ToString("N")[..8];
		}

		var used = taken.ToHashSet(StringComparer.Ordinal);
		if (!used.Contains(slug))
		{
			return slug;
		}

		for (var number = 2;; number++)
		{
			var suffix = "_" + number.ToString(CultureInfo.InvariantCulture);
			var candidate = Shorten(slug, MaxLength - suffix.Length) + suffix;
			if (!used.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	public static bool IsValid(string? slug)
	{
		return slug is { Length: > 0 and <= MaxLength }
			&& VariableNames.IsValid(slug)
			&& !slug.EndsWith('_')
			&& !slug.Contains("__", StringComparison.Ordinal);
	}

	private static string Shorten(string slug, int length)
	{
		return slug.Length <= length ? slug : slug[..length].TrimEnd('_');
	}
}
