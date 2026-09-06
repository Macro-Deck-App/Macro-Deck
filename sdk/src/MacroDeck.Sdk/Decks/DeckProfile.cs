namespace MacroDeck.Sdk.Decks;

/// <summary>A profile the deck can switch to, as offered to the user in a profile picker.</summary>
public sealed class DeckProfile
{
	public required string Id { get; init; }

	public required string Label { get; init; }
}
