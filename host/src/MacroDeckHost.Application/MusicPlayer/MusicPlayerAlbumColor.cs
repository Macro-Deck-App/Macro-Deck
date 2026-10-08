namespace MacroDeckHost.Application.MusicPlayer;

public interface IMusicPlayerAlbumColor
{
	IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances();

	Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default);
}

public sealed class MusicPlayerAlbumColor : IMusicPlayerAlbumColor
{
	private const int ArtworkSize = 64;

	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerStateCache _stateCache;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IArtworkPaletteExtractor _paletteExtractor;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	public MusicPlayerAlbumColor(
		IMusicPlayerRegistry registry,
		IMusicPlayerStateCache stateCache,
		IMusicPlayerArtworkService artworkService,
		IArtworkPaletteExtractor paletteExtractor)
	{
		_registry = registry;
		_stateCache = stateCache;
		_artworkService = artworkService;
		_paletteExtractor = paletteExtractor;
	}

	public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => _registry.GetInstances();

	public async Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		var state = _stateCache.GetState(instanceId);
		if (state is not { IsConnected: true, ArtworkId: { Length: > 0 } artworkId })
		{
			return Last(instanceId);
		}

		lock (_sync)
		{
			if (_entries.TryGetValue(instanceId, out var known) &&
				string.Equals(known.ArtworkId, artworkId, StringComparison.Ordinal))
			{
				return known.Color;
			}
		}

		var image = await _artworkService.GetImage(instanceId, artworkId, ArtworkSize, cancellationToken);
		var color = image is null ? null : _paletteExtractor.Extract(image.Content)?.Average;
		if (color is null)
		{
			return Last(instanceId);
		}

		lock (_sync)
		{
			_entries[instanceId] = new Entry(artworkId, color);
		}

		return color;
	}

	private string? Last(string instanceId)
	{
		lock (_sync)
		{
			return _entries.TryGetValue(instanceId, out var known) ? known.Color : null;
		}
	}

	private sealed record Entry(string ArtworkId, string Color);
}
