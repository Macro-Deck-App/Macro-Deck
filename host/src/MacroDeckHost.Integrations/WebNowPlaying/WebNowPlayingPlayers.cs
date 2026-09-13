using System.Buffers.Binary;
using System.Security.Cryptography;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal sealed record WebNowPlayingActivePlayer(
	WebNowPlayingPlayer Player,
	WebNowPlayingConnection Connection,
	string? ArtworkId);

internal sealed class WebNowPlayingPlayers
{
	private const int MaxPendingCovers = 64;

	private static readonly ILogger _logger =
		IntegrationLog.For<WebNowPlayingPlayers>(WebNowPlayingIntegration.IntegrationId);

	private readonly Lock _gate = new();
	private readonly HashSet<WebNowPlayingConnection> _connections = [];
	private readonly Dictionary<long, Entry> _players = [];
	private readonly Dictionary<(WebNowPlayingConnection Connection, long Id), byte[]> _pendingCovers = [];

	public bool HasConnections
	{
		get
		{
			lock (_gate)
			{
				return _connections.Count > 0;
			}
		}
	}

	public void AddConnection(WebNowPlayingConnection connection)
	{
		lock (_gate)
		{
			_connections.Add(connection);
		}
	}

	public void RemoveConnection(WebNowPlayingConnection connection)
	{
		lock (_gate)
		{
			_connections.Remove(connection);

			foreach (var id in _players.Where(pair => pair.Value.Connection == connection).Select(pair => pair.Key)
				.ToList())
			{
				_players.Remove(id);
			}

			foreach (var key in _pendingCovers.Keys.Where(key => key.Connection == connection).ToList())
			{
				_pendingCovers.Remove(key);
			}
		}
	}

	public bool HandleText(WebNowPlayingConnection connection, string text)
	{
		if (!WebNowPlayingProtocol.TryParse(text, out var message))
		{
			_logger.Debug("Ignoring an unreadable WebNowPlaying message");
			return false;
		}

		lock (_gate)
		{
			switch (message.Type)
			{
				case WebNowPlayingMessageType.PlayerAdded:
				{
					var player = WebNowPlayingProtocol.ApplyFields(
						new WebNowPlayingPlayer { Id = message.Id, PortId = message.Id },
						message.Data);
					_pendingCovers.Remove((connection, message.Id), out var cover);
					_players[message.Id] = new Entry(player, connection, cover, ArtworkIdOf(cover));
					return true;
				}
				case WebNowPlayingMessageType.PlayerUpdated:
				{
					if (!_players.TryGetValue(message.Id, out var entry) || entry.Connection != connection)
					{
						return false;
					}

					var player = WebNowPlayingProtocol.ApplyFields(entry.Player, message.Data);
					_players[message.Id] = player.CoverSource.Length == 0
						? entry with { Player = player, Cover = null, ArtworkId = null }
						: entry with { Player = player };
					return true;
				}
				case WebNowPlayingMessageType.PlayerRemoved:
					return _players.TryGetValue(message.Id, out var removed) &&
						removed.Connection == connection &&
						_players.Remove(message.Id);
				default:
					return false;
			}
		}
	}

	// The extension sends a cover before the line that adds its player, so a cover for an id not known
	// yet is held until that line arrives.
	public bool HandleCover(WebNowPlayingConnection connection, ReadOnlySpan<byte> message)
	{
		if (message.Length <= sizeof(uint))
		{
			return false;
		}

		long id = BinaryPrimitives.ReadUInt32LittleEndian(message);
		var cover = message[sizeof(uint)..].ToArray();

		lock (_gate)
		{
			if (_players.TryGetValue(id, out var entry) && entry.Connection == connection)
			{
				_players[id] = entry with { Cover = cover, ArtworkId = ArtworkIdOf(cover) };
				return true;
			}

			if (_pendingCovers.Count < MaxPendingCovers || _pendingCovers.ContainsKey((connection, id)))
			{
				_pendingCovers[(connection, id)] = cover;
			}

			return false;
		}
	}

	public WebNowPlayingActivePlayer? Active()
	{
		lock (_gate)
		{
			var playing = _players.Values.Where(entry => entry.Player.State == WebNowPlayingProtocol.StatePlaying)
				.ToList();
			var active = playing.Where(entry => entry.Player.Volume > 0).MaxBy(entry => entry.Player.ActiveAt) ??
				playing.MaxBy(entry => entry.Player.ActiveAt) ??
				_players.Values.MaxBy(entry => entry.Player.ActiveAt);

			return active is null
				? null
				: new WebNowPlayingActivePlayer(active.Player, active.Connection, active.ArtworkId);
		}
	}

	public byte[]? Artwork(string artworkId)
	{
		lock (_gate)
		{
			return _players.Values.FirstOrDefault(entry => entry.ArtworkId == artworkId)?.Cover;
		}
	}

	private static string? ArtworkIdOf(byte[]? cover)
		=> cover is null ? null : Convert.ToHexString(SHA256.HashData(cover)[..8]).ToLowerInvariant();

	private sealed record Entry(
		WebNowPlayingPlayer Player,
		WebNowPlayingConnection Connection,
		byte[]? Cover,
		string? ArtworkId);
}
