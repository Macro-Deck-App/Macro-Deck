namespace MacroDeck.Sdk.Decks;

/// <summary>A folder the deck can navigate to, as offered to the user in a folder picker.</summary>
public sealed class DeckFolder
{
	public required string Id { get; init; }

	/// <summary>Display label (profile-qualified when multiple profiles exist).</summary>
	public required string Label { get; init; }
}
