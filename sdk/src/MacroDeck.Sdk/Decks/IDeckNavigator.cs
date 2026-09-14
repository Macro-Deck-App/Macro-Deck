namespace MacroDeck.Sdk.Decks;

/// <summary>Navigates profiles and folders on connected deck clients.</summary>
public interface IDeckNavigator
{
	/// <summary>Changes to a folder. Set <paramref name="originClientId"/> to target one client.</summary>
	Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default);

	/// <summary>Changes to a profile's start folder. Set <paramref name="originClientId"/> to target one client.</summary>
	Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default);

	/// <summary>Moves to the parent folder.</summary>
	Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default);

	/// <summary>Moves to the previously shown folder.</summary>
	Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default);

	/// <summary>Returns folders available for pickers.</summary>
	IReadOnlyList<DeckFolder> GetFolders();

	/// <summary>Returns profiles available for pickers.</summary>
	IReadOnlyList<DeckProfile> GetProfiles();

	/// <summary>
	/// Where each connected client currently is. Empty on a host that does not report clients. A client
	/// leaves the list when it disconnects or, for a paired device, goes offline.
	/// </summary>
	IReadOnlyList<DeckClient> GetClients() => [];

	/// <summary>
	/// Raised when a client is first seen or moves to another folder or profile, never for a client that
	/// stays where it is. Not raised when a client leaves. In a plugin, handlers run on the connection's
	/// receive loop and see coalesced state: keep them short.
	/// </summary>
	event EventHandler<DeckClientChangedEventArgs>? ClientChanged
	{
		add { }
		remove { }
	}
}
