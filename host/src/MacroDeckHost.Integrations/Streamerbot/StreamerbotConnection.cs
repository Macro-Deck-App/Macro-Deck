using System.Text.Json;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot;

internal sealed class StreamerbotConnection : IDisposable
{
	private const string ApplicationEventSource = "Application";

	private static readonly ILogger _logger =
		IntegrationLog.For<StreamerbotConnection>(StreamerbotIntegration.IntegrationId);

	private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

	private static readonly TimeSpan _catalogRefreshDelay = TimeSpan.FromMilliseconds(500);

	private readonly Func<IStreamerbotClient> _clientFactory;
	private readonly Uri _uri;
	private readonly string? _password;
	private readonly StreamerbotEventEmitter? _events;
	private readonly TimeSpan _reconnectDelay;
	private readonly CancellationTokenSource _cts = new();

	private IStreamerbotClient? _client;
	private readonly Action? _onVariablesChanged;

	private volatile StreamerbotState _state = StreamerbotState.Disconnected;
	private volatile StreamerbotCatalog _catalog = StreamerbotCatalog.Empty;
	private volatile bool _authenticated;
	private int _failures;
	private int _refreshQueued;
	private bool _disposed;

	internal StreamerbotConnection(
		Func<IStreamerbotClient> clientFactory,
		Uri uri,
		string? password,
		StreamerbotEventEmitter? events = null,
		TimeSpan? reconnectDelay = null,
		Action? onVariablesChanged = null)
	{
		_clientFactory = clientFactory;
		_uri = uri;
		_password = password;
		_events = events;
		_onVariablesChanged = onVariablesChanged;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
	}

	public StreamerbotState State => _state;

	public StreamerbotCatalog Catalog => _catalog;

	public bool IsConnected => _state.IsConnected;

	public bool NeedsAuthentication { get; private set; }

	public void Start()
	{
		_ = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
	}

	public Task DoActionAsync(
		string action,
		IReadOnlyDictionary<string, object?>? arguments,
		CancellationToken cancellationToken = default)
	{
		var identity = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (Guid.TryParse(action, out _))
		{
			identity["id"] = action;
		}
		else
		{
			identity["name"] = action;
		}

		return SendAsync("DoAction",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["action"] = identity,
				["args"] = arguments
			},
			cancellationToken);
	}

	public Task ExecuteCodeTriggerAsync(
		string triggerName,
		IReadOnlyDictionary<string, object?>? arguments,
		CancellationToken cancellationToken = default)
		=> SendAsync("ExecuteCodeTrigger",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["triggerName"] = triggerName,
				["args"] = arguments
			},
			cancellationToken);

	public Task SendChatMessageAsync(
		string platform,
		string message,
		bool asBot,
		CancellationToken cancellationToken = default)
	{
		if (!_authenticated)
		{
			_logger.Warning("Streamer.bot chat message skipped: sending messages requires authentication. " +
				"Enable authentication in the Streamer.bot WebSocket server settings and set the password here.");
			return Task.CompletedTask;
		}

		return SendAsync("SendMessage",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["platform"] = platform,
				["message"] = message,
				["bot"] = asBot,
				["internal"] = true
			},
			cancellationToken);
	}

	public async Task<JsonElement?> GetGlobalAsync(
		string name,
		bool persisted,
		CancellationToken cancellationToken = default)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			_logger.Warning("Streamer.bot global '{Variable}' not read: not connected", name);
			return null;
		}

		try
		{
			var response = await client.RequestAsync("GetGlobal",
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["variable"] = name,
						["persisted"] = persisted
					},
					cancellationToken)
				.ConfigureAwait(false);

			return StreamerbotResponses.ReadGlobalValue(response, name);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Streamer.bot global '{Variable}' could not be read", name);
			return null;
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_client?.Dispose();
		_client = null;
		_state = StreamerbotState.Disconnected;
		_cts.Dispose();
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
			catch (StreamerbotAuthenticationException ex)
			{
				_logger.Warning("Streamer.bot authentication failed: {Reason}", ex.Message);
				NeedsAuthentication = true;
				return;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Streamer.bot session ended");
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
		var client = _clientFactory();
		var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnDisconnected(object? sender, string? reason) => closed.TrySetResult();

		client.Disconnected += OnDisconnected;
		client.EventReceived += OnEventReceived;
		_client = client;

		var announced = false;
		try
		{
			var hello = await client.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
			await AuthenticateAsync(client, hello, cancellationToken).ConfigureAwait(false);

			var info = hello?.Info ?? await LoadInfoAsync(client, cancellationToken).ConfigureAwait(false);
			await LoadCatalogAsync(client, cancellationToken).ConfigureAwait(false);
			await SubscribeAsync(client, cancellationToken).ConfigureAwait(false);
			var broadcaster = await LoadBroadcasterAsync(client, cancellationToken).ConfigureAwait(false);

			_state = new StreamerbotState
			{
				IsConnected = true,
				InstanceName = info?.Name,
				Version = info?.Version,
				BroadcasterName = broadcaster?.UserName,
				BroadcasterPlatform = broadcaster?.Platform
			};

			_failures = 0;
			announced = true;
			_onVariablesChanged?.Invoke();
			_events?.PublishConnected();
			_logger.Information("Connected to Streamer.bot {Version} at {Uri} with {ActionCount} actions",
				info?.Version ?? "(unknown version)",
				_uri,
				_catalog.Actions.Count);

			await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			client.EventReceived -= OnEventReceived;
			client.Disconnected -= OnDisconnected;
			_client = null;
			_authenticated = false;
			_state = StreamerbotState.Disconnected;
			_onVariablesChanged?.Invoke();

			try
			{
				await client.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the Streamer.bot session");
			}

			client.Dispose();

			if (announced)
			{
				_events?.PublishDisconnected();
			}
		}
	}

	private void OnEventReceived(object? sender, StreamerbotEventMessage message)
	{
		_events?.PublishEvent(message);

		if (string.Equals(message.Source, ApplicationEventSource, StringComparison.Ordinal))
		{
			ScheduleCatalogRefresh();
		}
	}

	private async Task AuthenticateAsync(
		IStreamerbotClient client,
		StreamerbotHello? hello,
		CancellationToken cancellationToken)
	{
		if (hello?.Authentication is not { } challenge)
		{
			return;
		}

		if (string.IsNullOrEmpty(_password))
		{
			try
			{
				await client.RequestAsync("GetInfo", cancellationToken: cancellationToken).ConfigureAwait(false);
				return;
			}
			catch (StreamerbotRequestException ex)
			{
				throw new StreamerbotAuthenticationException(
					"Streamer.bot requires a WebSocket password. Enter it in the integration setup.",
					ex);
			}
		}

		var response = StreamerbotAuthentication.CreateResponse(_password, challenge.Salt, challenge.Challenge);

		try
		{
			await client.RequestAsync("Authenticate",
					new Dictionary<string, object?>(StringComparer.Ordinal) { ["authentication"] = response },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (StreamerbotRequestException ex)
		{
			throw new StreamerbotAuthenticationException("Streamer.bot rejected the configured WebSocket password.",
				ex);
		}

		_authenticated = true;
	}

	private static async Task<StreamerbotInstanceInfo?> LoadInfoAsync(
		IStreamerbotClient client,
		CancellationToken cancellationToken)
	{
		var response = await client.RequestAsync("GetInfo", cancellationToken: cancellationToken).ConfigureAwait(false);
		return StreamerbotResponses.ReadInstanceInfo(response);
	}

	private static async Task<StreamerbotBroadcaster?> LoadBroadcasterAsync(
		IStreamerbotClient client,
		CancellationToken cancellationToken)
	{
		var response = await TryRequestAsync(client, "GetBroadcaster", null, cancellationToken).ConfigureAwait(false);
		return response is { } element ? StreamerbotResponses.ReadBroadcaster(element) : null;
	}

	private async Task LoadCatalogAsync(IStreamerbotClient client, CancellationToken cancellationToken)
	{
		var actions = await TryRequestAsync(client, "GetActions", null, cancellationToken).ConfigureAwait(false);
		var triggers = await TryRequestAsync(client, "GetCodeTriggers", null, cancellationToken).ConfigureAwait(false);
		var events = await TryRequestAsync(client, "GetEvents", null, cancellationToken).ConfigureAwait(false);

		var previous = _catalog;
		_catalog = new StreamerbotCatalog
		{
			Actions = actions is { } a ? StreamerbotResponses.ReadActions(a) : previous.Actions,
			CodeTriggers = triggers is { } t ? StreamerbotResponses.ReadCodeTriggers(t) : previous.CodeTriggers,
			Events = events is { } e ? StreamerbotResponses.ReadEventCatalog(e) : previous.Events
		};
	}

	private async Task SubscribeAsync(IStreamerbotClient client, CancellationToken cancellationToken)
	{
		var events = _catalog.Events;
		if (events.Count == 0)
		{
			_logger.Warning("Streamer.bot reported no events; triggers will not fire for this session");
			return;
		}

		await client.RequestAsync("Subscribe",
				new Dictionary<string, object?>(StringComparer.Ordinal) { ["events"] = events },
				cancellationToken)
			.ConfigureAwait(false);
	}

	private void ScheduleCatalogRefresh()
	{
		if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
		{
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				try
				{
					await Task.Delay(_catalogRefreshDelay, _cts.Token).ConfigureAwait(false);
				}
				finally
				{
					Interlocked.Exchange(ref _refreshQueued, 0);
				}

				if (_client is { IsConnected: true } client)
				{
					await LoadCatalogAsync(client, _cts.Token).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Streamer.bot catalogue refresh failed");
			}
		});
	}

	private async Task SendAsync(
		string request,
		IReadOnlyDictionary<string, object?> payload,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			_logger.Warning("Streamer.bot request '{Request}' ignored: not connected", request);
			return;
		}

		try
		{
			await client.RequestAsync(request, payload, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Streamer.bot request '{Request}' failed", request);
		}
	}

	private static async Task<JsonElement?> TryRequestAsync(
		IStreamerbotClient client,
		string request,
		IReadOnlyDictionary<string, object?>? payload,
		CancellationToken cancellationToken)
	{
		try
		{
			return await client.RequestAsync(request, payload, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Streamer.bot request '{Request}' failed", request);
			return null;
		}
	}

	private TimeSpan NextReconnectDelay()
	{
		var failures = Math.Min(Interlocked.Increment(ref _failures), 6);
		var seconds = _reconnectDelay.TotalSeconds * Math.Pow(2, failures - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _maxReconnectDelay.TotalSeconds));
	}
}
