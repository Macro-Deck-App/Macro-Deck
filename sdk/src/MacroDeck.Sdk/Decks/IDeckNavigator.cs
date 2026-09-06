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
}
