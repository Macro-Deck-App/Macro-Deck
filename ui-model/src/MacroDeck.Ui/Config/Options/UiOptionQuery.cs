namespace MacroDeck.Ui.Config.Options;

/// <summary>
/// What an option list is being asked for: the filter the user typed, and the sibling values the list depends
/// on. Both come from the existing dynamic-options request, where a list narrowed by another field - the
/// states a picked widget actually has - is refetched when that field changes, and a list fetched before it
/// changed is stale.
/// </summary>
public sealed record UiOptionQuery
{
	/// <summary>What the user has typed so far, or <c>null</c> when the list was not filtered.</summary>
	public string? Filter { get; init; }

	/// <summary>The sibling field values this list depends on, keyed by field name. Empty by default.
	/// </summary>
	public IReadOnlyDictionary<string, string> Values { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
