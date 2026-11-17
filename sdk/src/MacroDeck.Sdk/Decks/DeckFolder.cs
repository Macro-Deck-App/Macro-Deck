namespace MacroDeck.Sdk.Decks;

/// <summary>
/// A folder of a stored profile, as offered to the user in a folder picker. Folders of a virtual profile
/// that an integration provides are not listed.
/// </summary>
public sealed class DeckFolder
{
	public required string Id { get; init; }

	/// <summary>Display label (profile-qualified when multiple profiles exist).</summary>
	public required string Label { get; init; }

	/// <summary>The profile the folder belongs to, or <c>null</c> from a host that predates it.</summary>
	public string? ProfileId { get; init; }

	/// <summary>
	/// The folder this one is nested in, or <c>null</c> for a top-level folder and from a host that predates
	/// it. A parent is always in the same profile.
	/// </summary>
	public string? ParentId { get; init; }

	/// <summary>
	/// Where the folder sits among the folders that share its parent: lower comes first, as the folder tree
	/// in Macro Deck shows it. Values are a sort key, not necessarily contiguous, and equal values are
	/// ordered by <see cref="Id" />. <c>null</c> from a host that predates it.
	/// </summary>
	public int? Order { get; init; }
}
