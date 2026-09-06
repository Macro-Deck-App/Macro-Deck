namespace MacroDeck.Localization;

/// <summary>
/// A reference to a localized string together with its formatting arguments, resolved later - by the
/// host for its own messages, or by whichever renderer displays it, in that client's culture. Carrying
/// the intent rather than the text is what lets one shared UI session be rendered in two languages at
/// once, and what lets a language change take effect without rebuilding the producer's UI.
/// </summary>
public readonly record struct LocalizedString
{
	private static readonly IReadOnlyDictionary<string, object?> _noArguments =
		new Dictionary<string, object?>(StringComparer.Ordinal);

	/// <summary>Creates a reference with no formatting arguments.</summary>
	public LocalizedString(LocalizationKey key)
		: this(key, null)
	{
	}

	/// <summary>Creates a reference with formatting arguments, keyed by placeholder name.</summary>
	/// <param name="key">The key to resolve.</param>
	/// <param name="arguments">Values for the key's named placeholders. Null means none.</param>
	public LocalizedString(LocalizationKey key, IReadOnlyDictionary<string, object?>? arguments)
	{
		Key = key;
		Arguments = arguments ?? _noArguments;
	}

	/// <summary>The key to resolve.</summary>
	public LocalizationKey Key { get; }

	/// <summary>Values for the key's named placeholders, keyed by placeholder name. Never null.</summary>
	public IReadOnlyDictionary<string, object?> Arguments { get; }

	/// <summary>Value equality over the key and the argument contents.</summary>
	/// <remarks>Written out rather than left to the compiler: the generated record equality would compare
	/// <see cref="Arguments" /> by reference, so two references built from the same values on successive
	/// rebuilds would compare unequal and every UI property would look changed on every evaluation.</remarks>
	public bool Equals(LocalizedString other)
	{
		if (!Key.Equals(other.Key) || Arguments.Count != other.Arguments.Count)
		{
			return false;
		}

		foreach (var argument in Arguments)
		{
			if (!other.Arguments.TryGetValue(argument.Key, out var otherValue) ||
				!Equals(argument.Value, otherValue))
			{
				return false;
			}
		}

		return true;
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		// Order-independent: two equal argument maps may enumerate in different orders.
		var argumentsHash = 0;
		foreach (var argument in Arguments)
		{
			argumentsHash ^= HashCode.Combine(argument.Key, argument.Value);
		}

		return HashCode.Combine(Key, Arguments.Count, argumentsHash);
	}

	/// <summary>Renders the key and, when present, its argument names - for diagnostics only.</summary>
	public override string ToString()
		=> Arguments.Count == 0 ? Key.ToString() : $"{Key}({string.Join(", ", Arguments.Keys)})";
}
