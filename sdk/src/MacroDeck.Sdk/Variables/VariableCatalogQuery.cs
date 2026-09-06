namespace MacroDeck.Sdk.Variables;

/// <summary>
/// What the variable browser is asking a <see cref="IVariableProvider"/> to enumerate. Every member is
/// optional: a provider that cannot page or nest may ignore all of them and return its whole set, and
/// the host copes.
/// </summary>
public sealed record VariableCatalogQuery
{
	/// <summary>
	/// The node whose children are wanted, or <c>null</c> for the roots. A flat provider ignores this.
	/// </summary>
	public string? ParentId { get; init; }

	/// <summary>
	/// A user-typed substring, or <c>null</c> when the user is browsing rather than searching. Only ever
	/// set for a provider that reports <see cref="IVariableProvider.SupportsSearch"/>; matching is then
	/// that provider's own business - case, accent folding and which fields participate are all up to
	/// it. When a search is present the host does not assume the result is confined to
	/// <see cref="ParentId"/>: a provider may flatten the hierarchy for a search, which is usually what a
	/// user wants.
	/// </summary>
	public string? Search { get; init; }

	/// <summary>
	/// The <see cref="VariableCatalogPage.ContinuationToken"/> of the previous page, or <c>null</c> for
	/// the first. Opaque to the host, which stores and returns it without interpreting it, and only ever
	/// returns one this provider produced for this same <see cref="ParentId"/> and <see cref="Search"/>.
	/// </summary>
	public string? ContinuationToken { get; init; }

	/// <summary>
	/// How many items the caller would like. A provider may return fewer, and the host truncates a page
	/// that exceeds the protocol's page bound rather than failing it.
	/// </summary>
	public int PageSize { get; init; } = 100;
}
