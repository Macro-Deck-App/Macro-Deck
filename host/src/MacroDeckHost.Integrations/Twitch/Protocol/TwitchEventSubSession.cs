using System.Text.Json;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed record TwitchEventSubCallbacks(
	Func<string, CancellationToken, Task> SubscribeAsync,
	Action<TwitchEventSubMessage> Notification,
	Action<string, string> Revoked,
	Action<bool> ConnectionChanged,
	Action<string> AuthorizationLost);

internal sealed record TwitchEventSubOptions
{
	public Uri Endpoint { get; init; } = new("wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30");

	public TimeSpan BaseReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

	public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(1);

	public TimeSpan KeepaliveGrace { get; init; } = TimeSpan.FromSeconds(5);

	public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

	public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}

internal sealed class TwitchEventSubSession : IDisposable
{
	internal const int DeduplicationCapacity = 600;

	private static readonly TimeSpan _staleCutoff = TimeSpan.FromMinutes(10);

	private readonly Func<ITwitchEventSubClient> _clientFactory;
	private readonly TwitchEventSubCallbacks _callbacks;
	private readonly TwitchEventSubOptions _options;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _cts = new();

	private readonly Lock _sync = new();
	private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
	private readonly Queue<string> _seenOrder = new();

	private readonly List<ITwitchEventSubClient> _owned = [];

	private ITwitchEventSubClient? _client;
	private TaskCompletionSource? _sessionEnded;
	private Task? _loop;
	private bool _disposed;
	private bool _revoked;

	private long _lastMessageTicks;
	private long _keepaliveTicks = TimeSpan.FromSeconds(30).Ticks;

	public TwitchEventSubSession(
		Func<ITwitchEventSubClient> clientFactory,
		TwitchEventSubCallbacks callbacks,
		ILogger logger,
		TwitchEventSubOptions? options = null)
	{
		_clientFactory = clientFactory;
		_callbacks = callbacks;
		_logger = logger;
		_options = options ?? new TwitchEventSubOptions();
	}

	public bool IsConnected => _client?.IsConnected == true;

	internal Task Completion => _loop ?? Task.CompletedTask;

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_loop ??= Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();

		ITwitchEventSubClient? client;
		lock (_sync)
		{
			client = _client;
			_client = null;
			_sessionEnded?.TrySetResult();
		}

		client?.Dispose();
		_cts.Dispose();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var delay = TimeSpan.Zero;

		while (!cancellationToken.IsCancellationRequested)
		{
			if (delay > TimeSpan.Zero)
			{
				try
				{
					await _options.Delay(delay, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}

			var ended = await ConnectAndRunAsync(cancellationToken);
			if (ended is SessionOutcome.Fatal)
			{
				return;
			}

			delay = ended is SessionOutcome.Connected
				? _options.BaseReconnectDelay
				: NextDelay(delay);
		}
	}

	private TimeSpan NextDelay(TimeSpan current)
	{
		if (current <= TimeSpan.Zero)
		{
			return _options.BaseReconnectDelay;
		}

		var doubled = current * 2;
		return doubled > _options.MaxReconnectDelay ? _options.MaxReconnectDelay : doubled;
	}

	private async Task<SessionOutcome> ConnectAndRunAsync(CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var outcome = SessionOutcome.Failed;

		lock (_sync)
		{
			_owned.Clear();
			_owned.Add(client);
		}

		try
		{
			lock (_sync)
			{
				_sessionEnded = ended;
			}

			Attach(client, ended);

			var welcome = await client.ConnectAsync(_options.Endpoint, cancellationToken);
			lock (_sync)
			{
				_client = client;
			}

			Volatile.Write(ref _keepaliveTicks, welcome.Keepalive.Ticks);
			Volatile.Write(ref _lastMessageTicks, _options.Now().UtcTicks);

			await _callbacks.SubscribeAsync(welcome.SessionId, cancellationToken);
			_callbacks.ConnectionChanged(true);
			outcome = SessionOutcome.Connected;

			using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var watching = WatchdogAsync(ended, watchdog.Token);

			await ended.Task.WaitAsync(cancellationToken);
			await watchdog.CancelAsync();
			await watching;
		}
		catch (OperationCanceledException)
		{
			return SessionOutcome.Fatal;
		}
		catch (TwitchAuthorizationRevokedException ex)
		{
			_callbacks.AuthorizationLost(ex.Message);
			return SessionOutcome.Fatal;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "The Twitch EventSub session ended");
		}
		finally
		{
			lock (_sync)
			{
				if (ReferenceEquals(_client, client))
				{
					_client = null;
					_sessionEnded = null;
				}
			}

			if (outcome is SessionOutcome.Connected)
			{
				_callbacks.ConnectionChanged(false);
			}

			await CloseOwnedAsync();
		}

		return _revoked ? SessionOutcome.Fatal : outcome;
	}

	private void Attach(ITwitchEventSubClient client, TaskCompletionSource ended)
	{
		client.MessageReceived += (sender, message) => OnMessage(sender as ITwitchEventSubClient, message);
		client.Disconnected += (sender, reason) =>
		{
			// Only the socket that is still current ends the session. A socket retired by a
			// session_reconnect swap disconnects on purpose and must not restart anything.
			lock (_sync)
			{
				if (!ReferenceEquals(sender, _client) && _client is not null)
				{
					return;
				}
			}

			if (reason is not null)
			{
				_logger.Debug("The Twitch EventSub socket closed: {Reason}", reason);
			}

			ended.TrySetResult();
		};
	}

	private async Task WatchdogAsync(TaskCompletionSource ended, CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var limit = TimeSpan.FromTicks(Volatile.Read(ref _keepaliveTicks) * 2) + _options.KeepaliveGrace;
				await _options.Delay(limit / 2, cancellationToken);

				var silentFor = _options.Now().UtcTicks - Volatile.Read(ref _lastMessageTicks);
				if (silentFor <= limit.Ticks)
				{
					continue;
				}

				_logger.Debug("No Twitch EventSub traffic within {Seconds}s; treating the session as dead",
					limit.TotalSeconds);
				ended.TrySetResult();
				return;
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnMessage(ITwitchEventSubClient? sender, TwitchEventSubMessage message)
	{
		Volatile.Write(ref _lastMessageTicks, _options.Now().UtcTicks);

		switch (message.MessageType)
		{
			case TwitchEventSubMessageTypes.Keepalive:
				return;

			case TwitchEventSubMessageTypes.Reconnect:
				HandleReconnect(sender, message);
				return;

			case TwitchEventSubMessageTypes.Revocation:
				HandleRevocation(message);
				return;

			case TwitchEventSubMessageTypes.Notification:
				if (ShouldDeliver(message))
				{
					_callbacks.Notification(message);
				}

				return;

			default:
				return;
		}
	}

	private bool ShouldDeliver(TwitchEventSubMessage message)
	{
		if (_options.Now() - message.Timestamp > _staleCutoff)
		{
			return false;
		}

		if (string.IsNullOrEmpty(message.MessageId))
		{
			return true;
		}

		lock (_sync)
		{
			if (!_seen.Add(message.MessageId))
			{
				return false;
			}

			_seenOrder.Enqueue(message.MessageId);
			if (_seenOrder.Count > DeduplicationCapacity)
			{
				_seen.Remove(_seenOrder.Dequeue());
			}

			return true;
		}
	}

	private void HandleReconnect(ITwitchEventSubClient? sender, TwitchEventSubMessage message)
	{
		var url = ReadReconnectUrl(message.Payload);
		if (url is null)
		{
			_logger.Warning("Twitch asked for a reconnect without a URL; starting a fresh session");
			EndSession();
			return;
		}

		// Deliberately fire-and-forget: this runs on the read loop of the socket being replaced, and
		// awaiting the swap there would deadlock against the very messages it is waiting for.
		_ = Task.Run(() => SwapAsync(sender, url, _cts.Token), CancellationToken.None);
	}

	private async Task SwapAsync(ITwitchEventSubClient? previous, Uri url, CancellationToken cancellationToken)
	{
		var replacement = _clientFactory();

		TaskCompletionSource? ended;
		lock (_sync)
		{
			ended = _sessionEnded;
			_owned.Add(replacement);
		}

		if (ended is null)
		{
			replacement.Dispose();
			return;
		}

		try
		{
			Attach(replacement, ended);
			var welcome = await replacement.ConnectAsync(url, cancellationToken);

			lock (_sync)
			{
				_client = replacement;
			}

			Volatile.Write(ref _keepaliveTicks, welcome.Keepalive.Ticks);
			Volatile.Write(ref _lastMessageTicks, _options.Now().UtcTicks);

			await SafeDisconnect(previous);

			if (previous is not null)
			{
				lock (_sync)
				{
					_owned.Remove(previous);
				}

				previous.Dispose();
			}

			// Deliberately no re-subscribe: Twitch moves the subscriptions to the new session itself.
			_logger.Debug("Moved the Twitch EventSub session to its reconnect URL");
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not follow the Twitch EventSub reconnect; starting a fresh session");

			lock (_sync)
			{
				_owned.Remove(replacement);
			}

			replacement.Dispose();
			EndSession();
		}
	}

	private void HandleRevocation(TwitchEventSubMessage message)
	{
		var (type, status) = ReadRevocation(message.Payload);
		_callbacks.Revoked(type ?? message.SubscriptionType ?? "unknown", status ?? "unknown");

		switch (status)
		{
			case "authorization_revoked":
				_revoked = true;
				_callbacks.AuthorizationLost("Twitch revoked the authorization for this account.");
				EndSession();
				return;

			case "version_removed":
				_logger.Error("Twitch removed version {Version} of {Type}; the integration needs an update",
					message.SubscriptionVersion,
					type ?? message.SubscriptionType);
				return;

			default:
				_logger.Warning("Twitch revoked {Type}: {Status}", type ?? message.SubscriptionType, status);
				return;
		}
	}

	private void EndSession()
	{
		lock (_sync)
		{
			_sessionEnded?.TrySetResult();
		}
	}

	private async Task CloseOwnedAsync()
	{
		List<ITwitchEventSubClient> owned;
		lock (_sync)
		{
			owned = [.. _owned];
			_owned.Clear();
		}

		foreach (var client in owned)
		{
			await SafeDisconnect(client);
			client.Dispose();
		}
	}

	private async Task SafeDisconnect(ITwitchEventSubClient? client)
	{
		if (client is null)
		{
			return;
		}

		try
		{
			await client.DisconnectAsync();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while closing a Twitch EventSub socket");
		}
	}

	private static Uri? ReadReconnectUrl(JsonElement payload)
	{
		if (payload.ValueKind != JsonValueKind.Object ||
			!payload.TryGetProperty("session", out var session) ||
			session.ValueKind != JsonValueKind.Object ||
			!session.TryGetProperty("reconnect_url", out var url) ||
			url.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		return Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed) ? parsed : null;
	}

	private static (string? Type, string? Status) ReadRevocation(JsonElement payload)
	{
		if (payload.ValueKind != JsonValueKind.Object ||
			!payload.TryGetProperty("subscription", out var subscription) ||
			subscription.ValueKind != JsonValueKind.Object)
		{
			return (null, null);
		}

		return (ReadString(subscription, "type"), ReadString(subscription, "status"));
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private enum SessionOutcome
	{
		Failed,

		Connected,

		Fatal
	}
}
