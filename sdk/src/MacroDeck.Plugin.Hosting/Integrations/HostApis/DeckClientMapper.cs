using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Decks;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal static class DeckClientMapper
{
	public static DeckClient ToSdk(DeckClientDto dto)
		=> new() { ClientId = dto.ClientId, DeviceId = dto.DeviceId, ProfileId = dto.ProfileId, FolderId = dto.FolderId };
}
