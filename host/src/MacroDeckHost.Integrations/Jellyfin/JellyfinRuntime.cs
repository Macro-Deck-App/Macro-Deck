using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Integrations.Jellyfin;

internal sealed class JellyfinRuntime : IDisposable
{
	private const int ArtworkCapacity = 64;

	private readonly ConcurrentDictionary<string, (string ItemId, string? Tag)> _artwork = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<string> _artworkOrder = new();
	private readonly ConcurrentDictionary<string, JellyfinMusicPlayer> _devicePlayers = new(StringComparer.Ordinal);
	private readonly TimeProvider _time;

	public JellyfinRuntime(
		Guid entryId,
		string title,
		string variableKey,
		JellyfinRuntimeSettings settings,
		JellyfinConnection connection,
		JellyfinDeviceRegistry devices,
		string token,
		TimeProvider time)
	{
		EntryId = entryId;
		Title = title;
		VariableKey = variableKey;
		Settings = settings;
		Connection = connection;
		Devices = devices;
		Token = token;
		_time = time;
		ActivePlayer = new JellyfinMusicPlayer(this, null, time);
	}

	public Guid EntryId { get; }

	public string Title { get; }

	public string VariableKey { get; }

	public JellyfinRuntimeSettings Settings { get; }

	public string Token { get; }

	public JellyfinConnection Connection { get; }

	public JellyfinDeviceRegistry Devices { get; }

	public JellyfinMusicPlayer ActivePlayer { get; }

	public SemaphoreSlim PersistGate { get; } = new(1, 1);

	public string InstanceId => EntryId.ToString("N");

	public static string DeviceInstanceId(Guid entryId, string deviceLocalId) => $"{entryId:N}-{deviceLocalId}";

	public JellyfinMusicPlayer PlayerFor(string deviceId)
		=> _devicePlayers.GetOrAdd(deviceId, id => new JellyfinMusicPlayer(this, id, _time));

	public string RegisterArtwork(JellyfinMediaItem item)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{item.ArtworkItemId}|{item.ArtworkTag}"));
		var id = Convert.ToHexStringLower(hash.AsSpan(0, 8));
		if (_artwork.TryAdd(id, (item.ArtworkItemId, item.ArtworkTag)))
		{
			_artworkOrder.Enqueue(id);
			while (_artworkOrder.Count > ArtworkCapacity && _artworkOrder.TryDequeue(out var oldest))
			{
				_artwork.TryRemove(oldest, out _);
			}
		}

		return id;
	}

	public bool TryGetArtwork(string artworkId, out (string ItemId, string? Tag) artwork)
		=> _artwork.TryGetValue(artworkId, out artwork);

	public void Dispose() => Connection.Dispose();
}

internal sealed record JellyfinRuntimeSettings(string Url, string AuthMethod, string? Username, string DeviceId);
