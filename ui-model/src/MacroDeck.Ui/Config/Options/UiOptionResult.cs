namespace MacroDeck.Ui.Config.Options;

/// <summary>
/// What resolving an option list produced. Mirrors the existing dynamic-options response: the options, whether
/// a value outside them is accepted, how long the answer stays usable, and an error instead of a throw.
/// </summary>
public sealed record UiOptionResult
{
	/// <summary>The options, in the order the control shows them. Never reordered.</summary>
	public IReadOnlyList<UiOption> Options { get; init; } = [];

	/// <summary>Whether a value the list does not contain is accepted - what separates an autocomplete from a
	/// closed choice.</summary>
	public bool AllowsCustomValue { get; init; }

	/// <summary>How long this answer stays usable, as a hint to the client's cache. Emitted verbatim: the
	/// runtime owns no clock, so it neither caches nor expires anything itself.</summary>
	public int? CacheSeconds { get; init; }

	/// <summary>Why the list could not be resolved. Carried rather than thrown, so a provider that is down
	/// leaves a field with an error on it instead of a dialog that never finishes loading.</summary>
	public string? Error { get; init; }

	/// <summary>A successful result carrying <paramref name="options" />.</summary>
	public static UiOptionResult From(IReadOnlyList<UiOption> options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return new UiOptionResult { Options = options };
	}

	/// <summary>A failed result carrying <paramref name="error" /> and no options.</summary>
	public static UiOptionResult Failed(string error)
	{
		ArgumentException.ThrowIfNullOrEmpty(error);

		return new UiOptionResult { Error = error };
	}
}
