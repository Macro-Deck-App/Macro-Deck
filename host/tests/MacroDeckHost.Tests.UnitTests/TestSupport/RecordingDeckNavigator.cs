using MacroDeck.Sdk.Decks;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingDeckNavigator : IDeckNavigator
{
	public List<DeckFolder> Folders { get; init; } = [];
	public List<DeckProfile> Profiles { get; init; } = [];
	public List<DeckClient> Clients { get; init; } = [];
	public List<(string Action, string? OriginClientId)> Calls { get; } = [];

	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> Record("change-folder", originClientId);

	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> Record("change-profile", originClientId);

	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Record("go-to-parent", originClientId);

	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> Record("go-back", originClientId);

	public IReadOnlyList<DeckFolder> GetFolders() => Folders;

	public IReadOnlyList<DeckProfile> GetProfiles() => Profiles;

	public IReadOnlyList<DeckClient> GetClients() => Clients;

	private Task Record(string action, string? originClientId)
	{
		Calls.Add((action, originClientId));
		return Task.CompletedTask;
	}
}
