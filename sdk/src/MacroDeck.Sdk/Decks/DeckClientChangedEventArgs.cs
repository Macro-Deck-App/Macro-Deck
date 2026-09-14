namespace MacroDeck.Sdk.Decks;

/// <summary>Raised by <see cref="IDeckNavigator.ClientChanged" /> when a client is first seen or moves.</summary>
public sealed class DeckClientChangedEventArgs : EventArgs
{
	public DeckClientChangedEventArgs(DeckClient client, string? previousProfileId, string? previousFolderId)
	{
		Client = client;
		PreviousProfileId = previousProfileId;
		PreviousFolderId = previousFolderId;
	}

	/// <summary>The client's new position.</summary>
	public DeckClient Client { get; }

	/// <summary>The profile the client was on before, or <c>null</c> when the client was not known yet.</summary>
	public string? PreviousProfileId { get; }

	/// <summary>The folder the client was on before, or <c>null</c> when the client was not known yet.</summary>
	public string? PreviousFolderId { get; }
}
