namespace MacroDeck.Sdk.Actions;

// The key rule restates PluginBundledIconPacks.IsValidKey, which lives in the packaging assembly this
// package must not reference. The UI model keeps its own copy for the same reason.
internal static class PluginIconReferences
{
	private const int MaxKeyLength = 64;

	public static string Format(string key, string name)
	{
		if (!IsValidKey(key))
		{
			throw new ArgumentException(
				$"'{key}' is not a bundled icon pack key: use lowercase letters, digits and inner hyphens, at most {MaxKeyLength} characters.",
				nameof(key));
		}

		if (string.IsNullOrWhiteSpace(name) || name.Contains('/', StringComparison.Ordinal))
		{
			throw new ArgumentException("An icon name must not be blank or contain '/'.", nameof(name));
		}

		return $"{key}/{name}";
	}

	private static bool IsValidKey(string? key)
		=> key is { Length: > 0 and <= MaxKeyLength } &&
			key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') &&
			key[0] != '-' &&
			key[^1] != '-';
}
