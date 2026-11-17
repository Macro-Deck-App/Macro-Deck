namespace MacroDeck.Sdk.Decks;

/// <summary>Where one connected client currently is on the deck.</summary>
public sealed class DeckClient
{
	/// <summary>The client's id. Pass it as <c>originClientId</c> to navigate this client.</summary>
	public required string ClientId { get; init; }

	/// <summary>The paired device behind this client, or <c>null</c> when the client is not a paired device.</summary>
	public string? DeviceId { get; init; }

	public required string ProfileId { get; init; }

	public required string FolderId { get; init; }

	/// <summary>
	/// <c>true</c> when the client is a device connected through a device provider: the provider draws the deck
	/// itself, so the client never shows Macro Deck's own UI, such as notification dialogs. <c>false</c> for a
	/// web or app client, and for every client a Macro Deck that predates this member reports.
	/// </summary>
	public bool IsDeviceSession { get; init; }
}
