using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class MusicPlayerCatalogMapper
{
	public static MusicPlayerInstance ToDomain(MusicPlayerInstanceDto dto) => new(dto.Id, dto.DisplayName);

	public static MusicPlayerInstanceDto ToDto(MusicPlayerInstance instance, bool hasCatalog, bool hasDevices)
		=> new()
		{
			Id = instance.Id, DisplayName = instance.DisplayName, HasCatalog = hasCatalog, HasDevices = hasDevices
		};

	public static MusicPlayerCatalogItem ToDomain(MusicPlayerCatalogItemDto dto)
	{
		if (!Enum.TryParse<MusicPlayerCatalogItemKind>(dto.Kind, ignoreCase: false, out var kind))
		{
			throw new InvalidOperationException($"Unknown music player catalog item kind '{dto.Kind}'.");
		}

		return new MusicPlayerCatalogItem(dto.Id,
			dto.Title,
			kind,
			dto.Subtitle,
			dto.ArtworkId,
			dto.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
	}

	public static MusicPlayerCatalogItemDto ToDto(MusicPlayerCatalogItem item)
		=> new()
		{
			Id = item.Id,
			Title = item.Title,
			Kind = item.Kind.ToString(),
			Subtitle = item.Subtitle,
			ArtworkId = item.ArtworkId,
			DurationSeconds = item.Duration?.TotalSeconds
		};
}
