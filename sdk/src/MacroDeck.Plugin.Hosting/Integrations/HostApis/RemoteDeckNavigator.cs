using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IDeckNavigator"/> over <c>host.invoke</c> against <see cref="HostApis.Deck"/>.
/// The navigation members are request/response round trips - <see cref="IDeckNavigator"/> declares
/// them as returning <see cref="Task"/>, so the plugin genuinely awaits completion rather than a
/// fire-and-forget acknowledgement. <see cref="GetFolders"/> and <see cref="GetProfiles"/> are served
/// from <see cref="HostStateCache"/> instead - see its remarks.
/// </summary>
internal sealed class RemoteDeckNavigator(IHostInvoker invoker, HostStateCache stateCache) : IDeckNavigator
{
	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.ChangeFolder,
			new DeckChangeFolderArguments { FolderId = folderId, OriginClientId = originClientId },
			cancellationToken);

	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.ChangeProfile,
			new DeckChangeProfileArguments { ProfileId = profileId, OriginClientId = originClientId },
			cancellationToken);

	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.Parent,
			new DeckOriginArguments { OriginClientId = originClientId },
			cancellationToken);

	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.Back,
			new DeckOriginArguments { OriginClientId = originClientId },
			cancellationToken);

	public IReadOnlyList<DeckFolder> GetFolders() =>
		stateCache.Get<DeckStateDto>(Protocol.Callbacks.HostApis.Deck)?.Folders ?? [];

	public IReadOnlyList<DeckProfile> GetProfiles() =>
		stateCache.Get<DeckStateDto>(Protocol.Callbacks.HostApis.Deck)?.Profiles ?? [];
}
