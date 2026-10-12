namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// The <see cref="IMusicPlayerApi" /> of a context that offers no music players: always empty, never raises
/// <see cref="InstancesChanged" />, and knows no album colour.
/// </summary>
public sealed class UnsupportedMusicPlayerApi : IMusicPlayerApi
{
	public static readonly UnsupportedMusicPlayerApi Instance = new();

	private UnsupportedMusicPlayerApi()
	{
	}

	public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances => [];

	public event EventHandler? InstancesChanged
	{
		add { }
		remove { }
	}

	public Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);
		return Task.FromResult<string?>(null);
	}
}
