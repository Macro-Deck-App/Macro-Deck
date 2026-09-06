using System.Text.Json;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal sealed class YtmDesktopConnection : IDisposable
{
	private static readonly ILogger _logger =
		IntegrationLog.For<YtmDesktopConnection>(YtmDesktopIntegration.IntegrationId);

	private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

	private static readonly TimeSpan _playlistRefreshFloor = TimeSpan.FromSeconds(30);

	private readonly Func<IYtmDesktopRealtimeClient> _realtimeFactory;
	private readonly Func<IYtmDesktopApiClient> _apiFactory;
	private readonly YtmDesktopEndpoint _endpoint;
	private readonly string _token;
	private readonly YtmDesktopEventEmitter? _events;
	private readonly TimeSpan _reconnectDelay;
	private readonly TimeSpan _stateRefreshInterval;
	private readonly Func<string, string> _registerArtwork;
	private readonly CancellationTokenSource _cts = new();

	private readonly CancellationToken _lifetime;

	private readonly Lock _gate = new();

	private readonly YtmDesktopCommandSender _sender;

	private volatile YtmDesktopSnapshot _snapshot = YtmDesktopSnapshot.Disconnected;
	private volatile IYtmDesktopApiClient? _api;
	private volatile IYtmDesktopRealtimeClient? _realtime;
	private volatile List<YtmPlaylist>? _playlists;

	private Task? _pump;
	private bool _shuffleBelief;
	private bool _hasSnapshot;
	private long _lastStateTicks;
	private long _lastFetchTicks;
	private long _lastPlaylistTicks;
	private int _fetching;
	private int _failures;
	private bool _disposed;

	internal YtmDesktopConnection(
		Func<IYtmDesktopRealtimeClient> realtimeFactory,
		Func<IYtmDesktopApiClient> apiFactory,
		YtmDesktopEndpoint endpoint,
		string token,
		YtmDesktopEventEmitter? events = null,
		TimeSpan? reconnectDelay = null,
		TimeSpan? stateRefreshInterval = null,
		TimeSpan? commandInterval = null,
		Func<string, string>? registerArtwork = null)
	{
		_realtimeFactory = realtimeFactory;
		_apiFactory = apiFactory;
		_endpoint = endpoint;
		_token = token;
		_events = events;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
		_stateRefreshInterval = stateRefreshInterval ?? TimeSpan.FromSeconds(5);
		_registerArtwork = registerArtwork ?? (url => url);
		_lifetime = _cts.Token;
		_sender = new YtmDesktopCommandSender(SendCommandCoreAsync, commandInterval);
	}

	public YtmDesktopSnapshot Snapshot => _snapshot;

	public bool NeedsAuthorization { get; private set; }

	public bool IsSessionLive => _realtime?.IsConnected == true && _api is not null;

	public void Start()
	{
		_pump = Task.Run(() => PumpAsync(_lifetime), CancellationToken.None);
	}

	public async Task StopAsync()
	{
		if (_disposed)
		{
			return;
		}

		await _cts.CancelAsync().ConfigureAwait(false);

		if (_pump is { } pump)
		{
			_pump = null;

			try
			{
				await pump.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "The YouTube Music Desktop App pump ended with an error");
			}
		}

		Dispose();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_sender.Dispose();
		_realtime?.Dispose();
		_realtime = null;
		_api?.Dispose();
		_api = null;
		_snapshot = YtmDesktopSnapshot.Disconnected;
		_cts.Dispose();
	}

	public void TouchState()
	{
		if (_disposed || !IsSessionLive)
		{
			return;
		}

		var now = DateTime.UtcNow;
		if (now - new DateTime(Interlocked.Read(ref _lastStateTicks), DateTimeKind.Utc) < _stateRefreshInterval ||
			now - new DateTime(Interlocked.Read(ref _lastFetchTicks), DateTimeKind.Utc) < _stateRefreshInterval)
		{
			return;
		}

		if (Interlocked.CompareExchange(ref _fetching, 1, 0) != 0)
		{
			return;
		}

		Interlocked.Exchange(ref _lastFetchTicks, now.Ticks);
		_ = Task.Run(async () =>
			{
				try
				{
					await FetchStateAsync(_lifetime).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Background state refresh failed");
				}
				finally
				{
					Interlocked.Exchange(ref _fetching, 0);
				}
			},
			CancellationToken.None);
	}

	public Task SendCommandAsync(string command, object? data, CancellationToken cancellationToken)
		=> _sender.SendAsync(command, data, cancellationToken);

	public void EnqueueCommand(string command, object? data) => _sender.Enqueue(command, data);

	public bool ShouldSendShuffle(bool enabled)
	{
		lock (_gate)
		{
			if (_shuffleBelief == enabled)
			{
				return false;
			}

			_shuffleBelief = enabled;
		}

		var current = _snapshot;
		if (current.Player.IsConnected)
		{
			_snapshot = current with
			{
				Player = current.Player with { ShuffleEnabled = enabled }, ShuffleEnabled = enabled
			};
		}

		return true;
	}

	public void ApplyOptimisticVolume(int volumePercent)
	{
		var current = _snapshot;
		if (!current.Player.IsConnected)
		{
			return;
		}

		_snapshot = current with { Player = current.Player with { VolumePercent = Math.Clamp(volumePercent, 0, 100) } };
	}

	public IReadOnlyList<YtmPlaylist>? Playlists => _playlists;

	public async Task<IReadOnlyList<YtmPlaylist>> RefreshPlaylistsAsync(CancellationToken cancellationToken)
	{
		var api = _api ?? throw new InvalidOperationException("YouTube Music Desktop App is not connected.");

		var now = DateTime.UtcNow;
		if (now - new DateTime(Interlocked.Read(ref _lastPlaylistTicks), DateTimeKind.Utc) < _playlistRefreshFloor &&
			_playlists is { } cached)
		{
			return cached;
		}

		Interlocked.Exchange(ref _lastPlaylistTicks, now.Ticks);
		var playlists = await api.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false);
		var list = playlists.ToList();
		_playlists = list;
		return list;
	}

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RunSessionAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (YtmDesktopAuthorizationException ex)
			{
				_logger.Warning("YouTube Music Desktop App rejected the authorization: {Reason}", ex.Message);
				NeedsAuthorization = true;
				return;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "YouTube Music Desktop App session ended");
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			try
			{
				await Task.Delay(NextReconnectDelay(), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception)
			{
				return;
			}
		}
	}

	private async Task RunSessionAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var realtime = _realtimeFactory();
		var api = _apiFactory();
		api.UseToken(_token);

		var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnDisconnected(object? sender, string? reason) => closed.TrySetResult();

		realtime.Disconnected += OnDisconnected;
		realtime.StateUpdated += OnStateUpdated;
		realtime.PlaylistCreated += OnPlaylistCreated;
		realtime.PlaylistDeleted += OnPlaylistDeleted;

		try
		{
			await realtime.ConnectAsync(_endpoint.RealtimeUri(), _token, cancellationToken).ConfigureAwait(false);

			_api = api;
			_realtime = realtime;
			_failures = 0;

			lock (_gate)
			{
				_shuffleBelief = false;
			}

			_logger.Information("Connected to YouTube Music Desktop App at {Address}", _endpoint.DisplayAddress);

			await SeedStateAsync(api, cancellationToken).ConfigureAwait(false);
			await SeedPlaylistsAsync(api, cancellationToken).ConfigureAwait(false);

			await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			realtime.Disconnected -= OnDisconnected;
			realtime.StateUpdated -= OnStateUpdated;
			realtime.PlaylistCreated -= OnPlaylistCreated;
			realtime.PlaylistDeleted -= OnPlaylistDeleted;

			_realtime = null;
			_api = null;
			_playlists = null;
			_snapshot = YtmDesktopSnapshot.Disconnected;
			_hasSnapshot = false;
			Interlocked.Exchange(ref _lastStateTicks, 0);
			Interlocked.Exchange(ref _lastFetchTicks, 0);
			Interlocked.Exchange(ref _lastPlaylistTicks, 0);

			try
			{
				await realtime.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the YouTube Music Desktop App session");
			}

			realtime.Dispose();
			api.Dispose();
		}
	}

	private async Task SeedStateAsync(IYtmDesktopApiClient api, CancellationToken cancellationToken)
	{
		Interlocked.Exchange(ref _lastFetchTicks, DateTime.UtcNow.Ticks);

		try
		{
			var state = await api.GetStateAsync(cancellationToken).ConfigureAwait(false);
			Publish(state);
		}
		catch (YtmDesktopAuthorizationException)
		{
			throw;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the initial state; it will be retried");
		}
	}

	private async Task SeedPlaylistsAsync(IYtmDesktopApiClient api, CancellationToken cancellationToken)
	{
		Interlocked.Exchange(ref _lastPlaylistTicks, DateTime.UtcNow.Ticks);

		try
		{
			var playlists = await api.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false);
			_playlists = playlists.ToList();
		}
		catch (YtmDesktopAuthorizationException)
		{
			throw;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the playlists");
		}
	}

	private async Task FetchStateAsync(CancellationToken cancellationToken)
	{
		if (_api is not { } api)
		{
			return;
		}

		var state = await api.GetStateAsync(cancellationToken).ConfigureAwait(false);
		Publish(state);
	}

	private void OnStateUpdated(object? sender, JsonElement state) => Publish(state);

	private void Publish(JsonElement state)
	{
		YtmDesktopSnapshot next;
		try
		{
			bool shuffle;
			lock (_gate)
			{
				shuffle = _shuffleBelief;
			}

			next = YtmDesktopStateMapper.Map(YtmDesktopStateReader.Read(state), shuffle, _registerArtwork);
		}
		catch (Exception ex)
		{
			// On the read loop. A payload this integration cannot read must not end the session.
			_logger.Debug(ex, "Could not read a state update");
			return;
		}

		YtmDesktopSnapshot previous;
		bool hadSnapshot;

		lock (_gate)
		{
			previous = _snapshot;
			hadSnapshot = _hasSnapshot;
			_snapshot = next;
			_hasSnapshot = true;
		}

		Interlocked.Exchange(ref _lastStateTicks, DateTime.UtcNow.Ticks);

		if (hadSnapshot)
		{
			RaiseLikeChanged(previous, next);
		}
	}

	private void RaiseLikeChanged(YtmDesktopSnapshot previous, YtmDesktopSnapshot next)
	{
		if (previous.VideoId is not { } previousVideo ||
			next.VideoId is not { } nextVideo ||
			!string.Equals(previousVideo, nextVideo, StringComparison.Ordinal))
		{
			return;
		}

		if (previous.LikeStatus is not { } was || next.LikeStatus is not { } now || was == now)
		{
			return;
		}

		_events?.PublishLikeChanged(was, now, next.Player.TrackName, nextVideo);
	}

	private void OnPlaylistCreated(object? sender, YtmPlaylist playlist)
	{
		lock (_gate)
		{
			if (_playlists is not { } playlists)
			{
				return;
			}

			if (playlists.Any(p => string.Equals(p.Id, playlist.Id, StringComparison.Ordinal)))
			{
				return;
			}

			_playlists = [.. playlists, playlist];
		}
	}

	private void OnPlaylistDeleted(object? sender, string playlistId)
	{
		lock (_gate)
		{
			if (_playlists is not { } playlists)
			{
				return;
			}

			_playlists = playlists.Where(p => !string.Equals(p.Id, playlistId, StringComparison.Ordinal)).ToList();
		}
	}

	private Task SendCommandCoreAsync(string command, object? data, CancellationToken cancellationToken)
	{
		var api = _api ?? throw new InvalidOperationException("YouTube Music Desktop App is not connected.");
		return api.SendCommandAsync(command, data, cancellationToken);
	}

	private TimeSpan NextReconnectDelay()
	{
		var failures = Math.Min(Interlocked.Increment(ref _failures), 6);
		var seconds = _reconnectDelay.TotalSeconds * Math.Pow(2, failures - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _maxReconnectDelay.TotalSeconds));
	}
}
