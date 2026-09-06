using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The <c>host.state</c> payload pushed for <see cref="HostApis.Deck"/> - both pickers in one
/// push, since <c>IDeckNavigator.GetFolders()</c> and <c>GetProfiles()</c> change together whenever the
/// deck's structure does.</summary>
public sealed record DeckStateDto
{
	public IReadOnlyList<DeckFolder> Folders { get; init; } = [];

	public IReadOnlyList<DeckProfile> Profiles { get; init; } = [];
}
