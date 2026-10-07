using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.Jellyfin;

internal enum JellyfinConnectionStatus
{
	Connecting,
	Connected,
	Reconnecting,
	Disconnected,
	AuthenticationFailed
}

internal sealed record JellyfinServerState(
	JellyfinConnectionStatus Status,
	IReadOnlyList<JellyfinSession> Sessions,
	DateTimeOffset? LastSuccess)
{
	public static JellyfinServerState Initial { get; } = new(JellyfinConnectionStatus.Connecting, [], null);

	public bool IsConnected => Status == JellyfinConnectionStatus.Connected;

	public IReadOnlyList<JellyfinSession> ActiveSessions => [.. Sessions.Where(session => session.IsActive)];
}

internal sealed class JellyfinStateChangedEventArgs(JellyfinServerState previous, JellyfinServerState current)
	: EventArgs
{
	public JellyfinServerState Previous { get; } = previous;

	public JellyfinServerState Current { get; } = current;
}

internal sealed class JellyfinConnection : IDisposable
{
	private const int DisconnectedAfterFailures = 3;

	private static readonly ILogger _logger = IntegrationLog.For<JellyfinConnection>(JellyfinIntegration.IntegrationId);

	private readonly IJellyfinClient _client;
	private readonly string _ownDeviceId;
	private readonly TimeProvider _time;
	private readonly JellyfinConnectionTimings _timings;
	private readonly CancellationTokenSource _cts = new();
	private readonly Lock _sync = new();

	private JellyfinServerState _state = JellyfinServerState.Initial;
	private Task? _loop;

	public JellyfinConnection(IJellyfinClient client, string ownDeviceId, TimeProvider? time = null,
		JellyfinConnectionTimings? timings = null)
	{
		_client = client;
		_ownDeviceId = ownDeviceId;
		_time = time ?? TimeProvider.System;
		_timings = timings ?? JellyfinConnectionTimings.Default;
	}

	public event EventHandler<JellyfinStateChangedEventArgs>? StateChanged;

	public IJellyfinClient Client => _client;

	public JellyfinServerState State
	{
		get
		{
			lock (_sync)
			{
				return _state;
			}
		}
	}

	public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	public void PublishSessions(IReadOnlyList<JellyfinSessionDto> sessions)
		=> Update(current => new JellyfinServerState(JellyfinConnectionStatus.Connected,
			JellyfinSessionMapper.Map(sessions, _ownDeviceId, _time.GetUtcNow()),
			_time.GetUtcNow()));

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var failures = 0;
		var socketRetryAt = DateTimeOffset.MinValue;

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				PublishSessions(await _client.GetSessionsAsync(cancellationToken).ConfigureAwait(false));
				failures = 0;

				if (_time.GetUtcNow() < socketRetryAt)
				{
					await Task.Delay(_timings.PollInterval, _time, cancellationToken).ConfigureAwait(false);
					continue;
				}

				if (!await RunSocketAsync(cancellationToken).ConfigureAwait(false))
				{
					socketRetryAt = _time.GetUtcNow() + _timings.SocketRetryInterval;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (JellyfinAuthenticationException ex)
			{
				_logger.Warning("Jellyfin rejected the stored credentials: {Message}", ex.Message);
				SetStatus(JellyfinConnectionStatus.AuthenticationFailed);
				await DelayQuietly(_timings.AuthenticationRetryInterval, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				failures++;
				_logger.Debug(ex, "Jellyfin request failed ({Failures} in a row)", failures);
				SetStatus(failures >= DisconnectedAfterFailures
					? JellyfinConnectionStatus.Disconnected
					: JellyfinConnectionStatus.Reconnecting);
				await DelayQuietly(Backoff(failures), cancellationToken).ConfigureAwait(false);
			}
		}
	}

	// Returns false when the socket gave no session updates at all, which is how a server that refuses the
	// subscription (Jellyfin 10.10 with an API key) shows up; the connection then polls for a while.
	private async Task<bool> RunSocketAsync(CancellationToken cancellationToken)
	{
		var received = false;
		try
		{
			await _client.RunSessionSocketAsync(sessions =>
					{
						received = true;
						PublishSessions(sessions);
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (JellyfinSocketException ex)
		{
			_logger.Information(ex, "Jellyfin WebSocket unavailable; polling sessions instead");
			return false;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Jellyfin WebSocket session ended");
		}

		await DelayQuietly(_timings.SocketReconnectDelay, cancellationToken).ConfigureAwait(false);
		return received;
	}

	private TimeSpan Backoff(int failures)
	{
		var seconds = _timings.BackoffBase.TotalSeconds * Math.Pow(2, Math.Min(failures, 6) - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _timings.BackoffCap.TotalSeconds));
	}

	private async Task DelayQuietly(TimeSpan delay, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void SetStatus(JellyfinConnectionStatus status)
		=> Update(current => current.Status == status ? current : current with { Status = status });

	private void Update(Func<JellyfinServerState, JellyfinServerState> change)
	{
		JellyfinServerState previous;
		JellyfinServerState next;
		lock (_sync)
		{
			previous = _state;
			next = change(previous);
			if (ReferenceEquals(previous, next))
			{
				return;
			}

			_state = next;
		}

		try
		{
			StateChanged?.Invoke(this, new JellyfinStateChangedEventArgs(previous, next));
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "A Jellyfin state subscriber failed");
		}
	}
}

internal sealed record JellyfinConnectionTimings(
	TimeSpan PollInterval,
	TimeSpan SocketRetryInterval,
	TimeSpan SocketReconnectDelay,
	TimeSpan BackoffBase,
	TimeSpan BackoffCap,
	TimeSpan AuthenticationRetryInterval)
{
	public static JellyfinConnectionTimings Default { get; } = new(TimeSpan.FromSeconds(2),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5));
}
