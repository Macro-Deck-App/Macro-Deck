namespace MacroDeck.Sdk.Storage;

/// <summary>
/// The rules for <see cref="IIntegrationStorage" /> keys: 1 to <see cref="MaxLength" /> characters of
/// lowercase ASCII letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, starting with a letter or digit, and
/// not ending in one of the <see cref="ReservedSuffixes" /> storage uses for its own copies. Keys are file
/// names on every platform, so they are compared ordinally.
/// </summary>
public static class IntegrationStorageKeys
{
	public const int MaxLength = 64;

	public static IReadOnlyList<string> ReservedSuffixes { get; } = [".tmp", ".bak"];

	public static bool IsValid(string? key)
	{
		if (string.IsNullOrEmpty(key) || key.Length > MaxLength || !IsLeading(key[0]))
		{
			return false;
		}

		foreach (var character in key)
		{
			if (!IsLeading(character) && character is not ('.' or '_' or '-'))
			{
				return false;
			}
		}

		return !ReservedSuffixes.Any(suffix => key.EndsWith(suffix, StringComparison.Ordinal));
	}

	/// <exception cref="ArgumentException"><paramref name="key" /> is not a valid key.</exception>
	public static void Validate(string? key)
	{
		if (!IsValid(key))
		{
			throw new ArgumentException($"'{key}' is not a valid integration storage key.", nameof(key));
		}
	}

	private static bool IsLeading(char character)
	{
		return character is >= 'a' and <= 'z' or >= '0' and <= '9';
	}
}
