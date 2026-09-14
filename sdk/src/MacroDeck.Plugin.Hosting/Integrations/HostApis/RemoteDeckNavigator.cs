using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Decks;
using ILogger = Serilog.ILogger;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IDeckNavigator"/> over <c>host.invoke</c> against <see cref="HostApis.Deck"/>.
/// The navigation members are request/response round trips - <see cref="IDeckNavigator"/> declares
/// them as returning <see cref="Task"/>, so the plugin genuinely awaits completion rather than a
/// fire-and-forget acknowledgement. <see cref="GetFolders"/>, <see cref="GetProfiles"/> and
/// <see cref="GetClients"/> are served from <see cref="HostStateCache"/> instead - see its remarks.
/// </summary>
internal sealed class RemoteDeckNavigator : IDeckNavigator
{
	private readonly IHostInvoker _invoker;
	private readonly HostStateCache _stateCache;
	private readonly ILogger _logger;
	private readonly Lock _clientsGate = new();
	private IReadOnlyList<DeckClient> _clients;

	public RemoteDeckNavigator(IHostInvoker invoker,
		HostStateCache stateCache,
		PluginConnectionState connectionState,
		ILogger logger)
	{
		_invoker = invoker;
		_stateCache = stateCache;
		_logger = logger.ForContext<RemoteDeckNavigator>();
		_clients = ReadClients();
		stateCache.DeckChanged += OnDeckChanged;
		connectionState.Connected += OnConnected;
	}

	public event EventHandler<DeckClientChangedEventArgs>? ClientChanged;

	public Task ChangeFolderAsync(string folderId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.ChangeFolder,
			new DeckChangeFolderArguments { FolderId = folderId, OriginClientId = originClientId },
			cancellationToken);

	public Task ChangeProfileAsync(string profileId,
		string? originClientId = null,
		CancellationToken cancellationToken = default)
		=> _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.ChangeProfile,
			new DeckChangeProfileArguments { ProfileId = profileId, OriginClientId = originClientId },
			cancellationToken);

	public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.Parent,
			new DeckOriginArguments { OriginClientId = originClientId },
			cancellationToken);

	public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
		=> _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Deck,
			HostOperations.Deck.Back,
			new DeckOriginArguments { OriginClientId = originClientId },
			cancellationToken);

	public IReadOnlyList<DeckFolder> GetFolders() =>
		_stateCache.Get<DeckStateDto>(Protocol.Callbacks.HostApis.Deck)?.Folders ?? [];

	public IReadOnlyList<DeckProfile> GetProfiles() =>
		_stateCache.Get<DeckStateDto>(Protocol.Callbacks.HostApis.Deck)?.Profiles ?? [];

	public IReadOnlyList<DeckClient> GetClients()
	{
		lock (_clientsGate)
		{
			return _clients;
		}
	}

	private IReadOnlyList<DeckClient> ReadClients()
		=> [.. (_stateCache.Get<DeckStateDto>(Protocol.Callbacks.HostApis.Deck)?.Clients ?? []).Select(DeckClientMapper.ToSdk)];

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		if (e.Resumed)
		{
			return;
		}

		lock (_clientsGate)
		{
			_clients = [];
		}
	}

	private void OnDeckChanged()
	{
		var changes = new List<DeckClientChangedEventArgs>();
		lock (_clientsGate)
		{
			var before = new Dictionary<string, DeckClient>(StringComparer.Ordinal);
			foreach (var client in _clients)
			{
				before.TryAdd(client.ClientId, client);
			}

			var current = ReadClients();
			foreach (var client in current)
			{
				if (!before.TryGetValue(client.ClientId, out var previous))
				{
					changes.Add(new DeckClientChangedEventArgs(client, null, null));
				}
				else if (!string.Equals(previous.ProfileId, client.ProfileId, StringComparison.Ordinal) ||
					!string.Equals(previous.FolderId, client.FolderId, StringComparison.Ordinal))
				{
					changes.Add(new DeckClientChangedEventArgs(client, previous.ProfileId, previous.FolderId));
				}
			}

			_clients = current;
		}

		if (ClientChanged is not { } handlers)
		{
			return;
		}

		// Runs on the receive loop: a throwing plugin handler must not end the connection.
		foreach (var change in changes)
		{
			foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<DeckClientChangedEventArgs>>())
			{
				try
				{
					handler(this, change);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					_logger.Error(exception, "A ClientChanged handler failed for client {ClientId}", change.Client.ClientId);
				}
			}
		}
	}
}
