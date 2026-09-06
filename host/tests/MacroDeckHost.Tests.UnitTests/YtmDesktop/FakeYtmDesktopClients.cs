using System.Net;
using System.Text.Json;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

internal sealed class FakeYtmDesktopApiClient : IYtmDesktopApiClient
{
	private readonly Lock _gate = new();
	private readonly List<(string Command, object? Data)> _commands = [];

	private int _authCodeCalls;
	private int _tokenCalls;
	private int _stateCalls;
	private int _playlistCalls;
	private string? _usedToken;
	private bool _disposed;

	public List<string> ApiVersions { get; set; } = ["v1"];

	public string AuthCode { get; set; } = "1234";

	public string Token { get; set; } = "token";

	public string StateJson { get; set; } = EmptyState;

	public List<YtmPlaylist> Playlists { get; set; } = [];

	public Exception? MetadataException { get; set; }
	public Exception? AuthCodeException { get; set; }
	public Exception? TokenException { get; set; }
	public Exception? StateException { get; set; }
	public Exception? PlaylistException { get; set; }
	public Exception? CommandException { get; set; }

	public int AuthCodeCalls => Volatile.Read(ref _authCodeCalls);

	public int TokenCalls => Volatile.Read(ref _tokenCalls);

	public int StateCalls => Volatile.Read(ref _stateCalls);

	public int PlaylistCalls => Volatile.Read(ref _playlistCalls);

	public string? UsedToken
	{
		get
		{
			lock (_gate)
			{
				return _usedToken;
			}
		}
	}

	public IReadOnlyList<(string Command, object? Data)> Commands
	{
		get
		{
			lock (_gate)
			{
				return _commands.ToList();
			}
		}
	}

	public bool Disposed => Volatile.Read(ref _disposed);

	public const string EmptyState
		= """{"player":{"trackState":-1,"videoProgress":0,"volume":50,"adPlaying":false,"queue":null},"video":null,"playlistId":null}""";

	public void UseToken(string? token)
	{
		lock (_gate)
		{
			_usedToken = token;
		}
	}

	public Task<IReadOnlyList<string>> GetApiVersionsAsync(CancellationToken cancellationToken)
		=> MetadataException is not null
			? Task.FromException<IReadOnlyList<string>>(MetadataException)
			: Task.FromResult<IReadOnlyList<string>>(ApiVersions.ToList());

	public Task<string> RequestAuthCodeAsync(
		string appId,
		string appName,
		string appVersion,
		CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _authCodeCalls);
		return AuthCodeException is not null
			? Task.FromException<string>(AuthCodeException)
			: Task.FromResult(AuthCode);
	}

	public Task<string> RequestTokenAsync(string appId, string code, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _tokenCalls);
		return TokenException is not null ? Task.FromException<string>(TokenException) : Task.FromResult(Token);
	}

	public Task<JsonElement> GetStateAsync(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _stateCalls);
		if (StateException is not null)
		{
			return Task.FromException<JsonElement>(StateException);
		}

		using var document = JsonDocument.Parse(StateJson);
		return Task.FromResult(document.RootElement.Clone());
	}

	public Task<IReadOnlyList<YtmPlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _playlistCalls);
		return PlaylistException is not null
			? Task.FromException<IReadOnlyList<YtmPlaylist>>(PlaylistException)
			: Task.FromResult<IReadOnlyList<YtmPlaylist>>(Playlists.ToList());
	}

	public Task SendCommandAsync(string command, object? data, CancellationToken cancellationToken)
	{
		if (CommandException is not null)
		{
			return Task.FromException(CommandException);
		}

		lock (_gate)
		{
			_commands.Add((command, data));
		}

		return Task.CompletedTask;
	}

	public void Dispose() => Volatile.Write(ref _disposed, true);

	public static YtmDesktopApiException RateLimited()
		=> new("Too many requests", (HttpStatusCode)429, retryAfter: TimeSpan.FromSeconds(5));
}

internal sealed class FakeYtmDesktopRealtimeClient : IYtmDesktopRealtimeClient
{
	private int _connectCalls;
	private bool _isConnected;
	private bool _disposed;

	public Exception? ConnectException { get; set; }

	public int ConnectCalls => Volatile.Read(ref _connectCalls);

	public bool IsConnected => Volatile.Read(ref _isConnected);

	public bool Disposed => Volatile.Read(ref _disposed);

	public event EventHandler<JsonElement>? StateUpdated;
	public event EventHandler<YtmPlaylist>? PlaylistCreated;
	public event EventHandler<string>? PlaylistDeleted;
	public event EventHandler<string?>? Disconnected;

	public Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _connectCalls);
		if (ConnectException is not null)
		{
			return Task.FromException(ConnectException);
		}

		Volatile.Write(ref _isConnected, true);
		return Task.CompletedTask;
	}

	public Task DisconnectAsync()
	{
		Volatile.Write(ref _isConnected, false);
		return Task.CompletedTask;
	}

	public void RaiseState(string json)
	{
		using var document = JsonDocument.Parse(json);
		StateUpdated?.Invoke(this, document.RootElement.Clone());
	}

	public void RaisePlaylistCreated(YtmPlaylist playlist) => PlaylistCreated?.Invoke(this, playlist);

	public void RaisePlaylistDeleted(string playlistId) => PlaylistDeleted?.Invoke(this, playlistId);

	public void Drop(string? reason = "closed")
	{
		Volatile.Write(ref _isConnected, false);
		Disconnected?.Invoke(this, reason);
	}

	public void Dispose() => Volatile.Write(ref _disposed, true);
}
