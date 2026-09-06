namespace MacroDeck.Sdk.Variables;

/// <summary>One page of <see cref="IVariableProvider.DiscoverAsync"/> results.</summary>
public sealed record VariableCatalogPage
{
	/// <summary>
	/// A provider with nothing to enumerate right now - not connected, no resources, past the last page.
	/// </summary>
	public static readonly VariableCatalogPage Empty = new() { Items = [] };

	public required IReadOnlyList<VariableDefinition> Items { get; init; }

	/// <summary>
	/// Pass back in <see cref="VariableCatalogQuery.ContinuationToken"/> to get the next page.
	/// <c>null</c> means this was the last one.
	///
	/// <para>
	/// A continuation token rather than an offset because a catalog mutates while the user browses:
	/// an offset over a set that gained or lost an entry silently skips and duplicates items, while a
	/// token lets each provider carry whatever cursor its source actually has. Opaque to everything but
	/// the provider that produced it, and never persisted - a token only has to survive the browsing
	/// session it was issued in.
	/// </para>
	/// </summary>
	public string? ContinuationToken { get; init; }
}
