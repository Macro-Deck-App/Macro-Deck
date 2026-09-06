using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal sealed class HomeAssistantConnection : IDisposable
{
	internal const int UnreachableAfterFailures = 5;

	private const string StateChangedEventType = "state_changed";

	private static readonly string[] _catalogChangeEventTypes =
	[
		"service_registered",
		"service_removed",
		"area_registry_updated",
		"device_registry_updated",
		"entity_registry_updated"
	];

	private static readonly ILogger _logger =
		IntegrationLog.For<HomeAssistantConnection>(HomeAssistantIntegration.IntegrationId);

	private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

	private static readonly TimeSpan _catalogRefreshDelay = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(30);

	private readonly Func<IHomeAssistantClient> _clientFactory;
	private readonly Uri _uri;
	private readonly string _token;
	private readonly HomeAssistantEventEmitter? _events;
	private readonly HomeAssistantVariableCatalog? _dynamicVariables;
	private readonly TimeSpan _reconnectDelay;
	private readonly CancellationTokenSource _cts = new();

	// Reassigned wholesale by SeedEntities on every (re)connect rather than cleared and refilled in place,
	// so a reader (including HomeAssistantCatalog.Entities, which aliases this field) always sees either
	// the complete previous snapshot or the complete new one - never a transient empty dictionary that
	// would flap every bound resource to unavailable while the fresh state trickles back in.
	private volatile ConcurrentDictionary<string, HomeAssistantEntityState> _entities
		= new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, byte> _observedEventTypes = new(StringComparer.Ordinal);

	private IHomeAssistantClient? _client;
	private volatile HomeAssistantState _state = HomeAssistantState.Disconnected;
	private volatile HomeAssistantCatalog _catalog = HomeAssistantCatalog.Empty;
	private volatile IReadOnlyList<string> _missingEntities = [];

	// Replaced wholesale by UpdateWatchedEntities as the host's bound resource set changes, rather than
	// fixed at construction: the watched-entity config-flow step is gone, so what "watched" means now
	// comes from HomeAssistantVariableCatalog.SubscribeAsync instead of a one-time selection.
	private volatile IReadOnlyList<string> _watchedEntities;

	private int _failures;
	private int _refreshQueued;
	private int _disposed;

	internal HomeAssistantConnection(
		Func<IHomeAssistantClient> clientFactory,
		Uri uri,
		string token,
		IReadOnlyList<string>? watchedEntities = null,
		HomeAssistantEventEmitter? events = null,
		HomeAssistantVariableCatalog? dynamicVariables = null,
		TimeSpan? reconnectDelay = null)
	{
		_clientFactory = clientFactory;
		_uri = uri;
		_token = token;
		_watchedEntities = watchedEntities ?? [];
		_events = events;
		_dynamicVariables = dynamicVariables;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
	}

	public HomeAssistantState State => _state;

	public HomeAssistantCatalog Catalog => _catalog;

	public bool IsConnected => _state.IsConnected;

	public Uri Uri => _uri;

	public bool AuthInvalid { get; private set; }

	public bool CertificateUntrusted { get; private set; }

	public bool Unreachable => _failures >= UnreachableAfterFailures;

	public IReadOnlyList<string> MissingEntities => _missingEntities;

	public IEnumerable<string> ObservedEventTypes => _observedEventTypes.Keys;

	public void Start()
	{
		_ = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
	}

	/// <summary>
	/// Replaces the set of entities the "missing entity" issue watches for, driven by
	/// <see cref="HomeAssistantVariableCatalog"/>'s current working set rather than a value fixed when
	/// the connection was created. Safe to call at any time, connected or not.
	/// </summary>
	public void UpdateWatchedEntities(IReadOnlyCollection<string> entityIds)
	{
		_watchedEntities = entityIds as IReadOnlyList<string> ?? entityIds.ToList();
		_missingEntities = FindMissingEntities();
	}

	public HomeAssistantEntityState? Entity(string? entityId)
		=> entityId is { Length: > 0 } id ? _entities.GetValueOrDefault(id) : null;

	public async Task<LocalizedText?> CallServiceAsync(
		string domain,
		string service,
		IReadOnlyDictionary<string, object?>? target,
		IReadOnlyDictionary<string, object?>? data,
		CancellationToken cancellationToken = default)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			return AppStrings.Integrations.HomeAssistant.Errors.NotConnected();
		}

		try
		{
			await client.SendCommandAsync("call_service",
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["domain"] = domain,
						["service"] = service,
						["service_data"] = data,
						["target"] = target
					},
					cancellationToken)
				.ConfigureAwait(false);

			return null;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (HomeAssistantRequestException ex)
		{
			_logger.Warning(ex, "Home Assistant rejected {Domain}.{Service}", domain, service);
			return AppStrings.Integrations.HomeAssistant.Errors.ServiceCallRejected(domain: domain,
				service: service,
				details: ex.Message);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Home Assistant call {Domain}.{Service} failed", domain, service);
			return AppStrings.Integrations.HomeAssistant.Errors.ServiceCallFailed(domain: domain, service: service);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		_cts.Cancel();
		_client?.Dispose();
		_client = null;
		_state = HomeAssistantState.Disconnected;
		_cts.Dispose();
	}

	private static IReadOnlyList<string> BuildDomains(
		IReadOnlyDictionary<string, IReadOnlyList<string>> services,
		IEnumerable<string> entityIds)
	{
		var domains = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var domain in services.Keys)
		{
			domains.Add(domain);
		}

		foreach (var entityId in entityIds)
		{
			if (HomeAssistantEntityState.DomainOf(entityId) is { Length: > 0 } domain)
			{
				domains.Add(domain);
			}
		}

		return [.. domains];
	}

	private static async Task<JsonElement?> TryCommandAsync(
		IHomeAssistantClient client,
		string type,
		CancellationToken cancellationToken)
	{
		try
		{
			return await client.SendCommandAsync(type, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Home Assistant command '{Command}' failed", type);
			return null;
		}
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
			catch (HomeAssistantAuthenticationException ex)
			{
				_logger.Warning("Home Assistant rejected the access token: {Reason}", ex.Message);
				AuthInvalid = true;
				return;
			}
			catch (HomeAssistantTlsException ex)
			{
				// A certificate can be renewed or trusted without touching Macro Deck, so this keeps
				// retrying - it only stops being silent about why.
				_logger.Warning("Home Assistant certificate validation failed: {Reason}", ex.Message);
				CertificateUntrusted = true;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Home Assistant session ended");
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

		using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task? ping = null;
		var announced = false;

		try
		{
			var hello = await client.ConnectAsync(_uri, _token, cancellationToken).ConfigureAwait(false);

			await client.SendCommandAsync("subscribe_events", cancellationToken: cancellationToken)
				.ConfigureAwait(false);

			var states = HomeAssistantResponses.ReadStates(await client
				.SendCommandAsync("get_states", cancellationToken: cancellationToken)
				.ConfigureAwait(false));

			_failures = 0;
			CertificateUntrusted = false;

			SeedEntities(states);

			var config = await TryCommandAsync(client, "get_config", cancellationToken).ConfigureAwait(false);
			var (locationName, version) = config is { } element
				? HomeAssistantResponses.ReadConfig(element)
				: (null, null);

			await LoadCatalogAsync(client, cancellationToken).ConfigureAwait(false);

			_state = new HomeAssistantState
			{
				IsConnected = true,
				Version = hello.Version ?? version,
				LocationName = locationName,
				EntityCount = _entities.Count
			};

			_missingEntities = FindMissingEntities();

			if (_dynamicVariables is { } dynamicVariables)
			{
				// Only the resources actually subscribed to, not every entity in the catalogue: a reconnect
				// can carry 1500-3000 entities' worth of state and attributes, and the host is bound to a
				// handful of them. Awaited too, rather than fire-and-forget, so a publish failure surfaces
				// through this session the same way any other reconnect failure does.
				await dynamicVariables.PublishSubscribedAsync(cancellationToken).ConfigureAwait(false);
			}

			announced = true;
			_events?.PublishConnected();
			_logger.Information("Connected to Home Assistant {Version} at {Uri} with {EntityCount} entities",
				_state.Version ?? "(unknown version)",
				_uri,
				_entities.Count);

			ping = Task.Run(() => PingLoopAsync(client, pingCts.Token), CancellationToken.None);

			await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			await pingCts.CancelAsync().ConfigureAwait(false);
			if (ping is not null)
			{
				try
				{
					await ping.ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Home Assistant ping loop ended with an error");
				}
			}

			client.EventReceived -= OnEventReceived;
			client.Disconnected -= OnDisconnected;
			_client = null;
			_state = HomeAssistantState.Disconnected;

			try
			{
				await client.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the Home Assistant session");
			}

			client.Dispose();

			if (announced)
			{
				_events?.PublishDisconnected();
			}
		}
	}

	private static async Task PingLoopAsync(IHomeAssistantClient client, CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_pingInterval);

		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				await client.SendCommandAsync("ping", cancellationToken: cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Home Assistant ping failed; closing the session");
			await client.DisconnectAsync().ConfigureAwait(false);
		}
	}

	private void OnEventReceived(object? sender, HomeAssistantEventMessage message)
	{
		_observedEventTypes.TryAdd(message.EventType, 0);

		if (string.Equals(message.EventType, StateChangedEventType, StringComparison.Ordinal))
		{
			HandleStateChanged(message.Data);
		}
		else if (Array.IndexOf(_catalogChangeEventTypes, message.EventType) >= 0)
		{
			ScheduleCatalogRefresh();
		}

		_events?.PublishEvent(message);
	}

	private void HandleStateChanged(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("entity_id", out var entityIdElement) ||
			entityIdElement.ValueKind != JsonValueKind.String ||
			entityIdElement.GetString() is not { Length: > 0 } entityId)
		{
			return;
		}

		var newState = data.TryGetProperty("new_state", out var newElement)
			? HomeAssistantResponses.ReadState(newElement)
			: null;
		var fromState = data.TryGetProperty("old_state", out var oldElement)
			? HomeAssistantResponses.ReadState(oldElement)?.State
			: null;

		if (newState is null)
		{
			_entities.TryRemove(entityId, out _);
		}
		else
		{
			_entities[entityId] = newState;
			_dynamicVariables?.Push(newState);
		}

		_events?.PublishStateChanged(entityId, newState, fromState, _catalog.AreaOf(entityId));
	}

	private void SeedEntities(IReadOnlyList<HomeAssistantEntityState> states)
	{
		var entities = new ConcurrentDictionary<string, HomeAssistantEntityState>(StringComparer.Ordinal);
		foreach (var state in states)
		{
			entities[state.EntityId] = state;
		}

		// A single reference assignment, not Clear()-then-refill on the dictionary readers already hold:
		// see the field's own remarks for why that distinction matters.
		_entities = entities;
	}

	private List<string> FindMissingEntities()
	{
		var missing = new List<string>();
		foreach (var entityId in _watchedEntities)
		{
			if (!_entities.ContainsKey(entityId))
			{
				missing.Add(entityId);
			}
		}

		return missing;
	}

	private async Task LoadCatalogAsync(IHomeAssistantClient client, CancellationToken cancellationToken)
	{
		var services = await TryCommandAsync(client, "get_services", cancellationToken).ConfigureAwait(false);
		var areas = await TryCommandAsync(client, "config/area_registry/list", cancellationToken)
			.ConfigureAwait(false);
		var devices = await TryCommandAsync(client, "config/device_registry/list", cancellationToken)
			.ConfigureAwait(false);
		var entityRegistry = await TryCommandAsync(client, "config/entity_registry/list", cancellationToken)
			.ConfigureAwait(false);

		var previous = _catalog;
		var serviceMap = services is { } s ? HomeAssistantResponses.ReadServices(s) : previous.Services;
		var areaList = areas is { } a ? HomeAssistantResponses.ReadAreas(a) : previous.Areas;
		var deviceList = devices is { } d ? HomeAssistantResponses.ReadDevices(d) : previous.Devices;
		var entityAreas = entityRegistry is { } e
			? HomeAssistantResponses.ReadEntityAreas(e, areaList, deviceList)
			: previous.EntityAreas;

		_catalog = new HomeAssistantCatalog
		{
			Entities = _entities,
			Domains = BuildDomains(serviceMap, _entities.Keys),
			Services = serviceMap,
			Areas = areaList,
			Devices = deviceList,
			EntityAreas = entityAreas
		};
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
				_logger.Debug(ex, "Home Assistant catalogue refresh failed");
			}
		});
	}

	private TimeSpan NextReconnectDelay()
	{
		var failures = Math.Min(Interlocked.Increment(ref _failures), 6);
		var seconds = _reconnectDelay.TotalSeconds * Math.Pow(2, failures - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _maxReconnectDelay.TotalSeconds));
	}
}
