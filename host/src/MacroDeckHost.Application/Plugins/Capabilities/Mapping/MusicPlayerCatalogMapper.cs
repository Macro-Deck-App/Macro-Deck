using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class MusicPlayerCatalogMapper
{
	public static MusicPlayerInstance ToDomain(MusicPlayerInstanceDto dto)
		=> new(dto.Id, dto.DisplayName) { Options = OptionsToDomain(dto.Options) };

	public static MusicPlayerInstanceDto ToDto(MusicPlayerInstance instance, bool hasCatalog, bool hasDevices)
		=> new()
		{
			Id = instance.Id,
			DisplayName = instance.DisplayName,
			HasCatalog = hasCatalog,
			HasDevices = hasDevices,
			Options = [.. instance.Options.Select(ActionParameterMapper.ToDto)]
		};

	// An option of a kind this host does not know costs only that option: a newer plugin must not lose
	// its whole instance list, and the host could not render the option anyway.
	private static List<ActionParameter> OptionsToDomain(IReadOnlyList<ActionParameterDto>? options)
	{
		var mapped = new List<ActionParameter>(options?.Count ?? 0);

		foreach (var option in options ?? [])
		{
			try
			{
				mapped.Add(ActionParameterMapper.ToDomain(option));
			}
			catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
				or FormatException)
			{
			}
		}

		return mapped;
	}

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
