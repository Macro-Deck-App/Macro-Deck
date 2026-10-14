using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteMusicPlayerApi : IMusicPlayerApi, IDisposable
{
	private readonly HostStateCache _stateCache;
	private readonly IHostInvoker _invoker;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<Subscription> _subscriptions = [];
	private IReadOnlyList<MusicPlayerInstanceSnapshot> _instances = [];
	private Task _raising = Task.CompletedTask;

	public RemoteMusicPlayerApi(HostStateCache stateCache, IHostInvoker invoker, ILogger logger)
	{
		_stateCache = stateCache;
		_invoker = invoker;
		_logger = logger.ForContext<RemoteMusicPlayerApi>();
		Lifecycle = new LifecycleView(this);
		_stateCache.MusicPlayersChanged += OnMusicPlayersChanged;
		Update();
	}

	public IMusicPlayerApi Lifecycle { get; }

	public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances
	{
		get
		{
			lock (_gate)
			{
				return _instances;
			}
		}
	}

	public event EventHandler? InstancesChanged
	{
		add => Subscribe(value, false);
		remove => Unsubscribe(value, false);
	}

	public async Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);

		JsonElement? result;
		try
		{
			result = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.MusicPlayers,
				HostOperations.MusicPlayers.AlbumColor,
				new MusicPlayerAlbumColorArguments { InstanceId = instanceId },
				cancellationToken);
		}
		catch (HostInvocationException exception)
		{
			_logger.Debug(exception, "The host did not answer the album colour of {InstanceId}", instanceId);
			return null;
		}

		try
		{
			return result?.Deserialize<MusicPlayerAlbumColorResult>(PluginProtocolJson.Options)?.Color is
				{ Length: > 0 } color
				? color
				: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public void ReleaseLifecycleHandlers()
	{
		lock (_gate)
		{
			_subscriptions.RemoveAll(subscription => subscription.Lifecycle);
		}
	}

	public void Dispose()
	{
		_stateCache.MusicPlayersChanged -= OnMusicPlayersChanged;
	}

	private void OnMusicPlayersChanged()
	{
		if (!Update())
		{
			return;
		}

		// Raised off the receive loop and in push order, so a slow or throwing handler cannot stall the connection.
		lock (_gate)
		{
			_raising = _raising.ContinueWith(_ => Raise(),
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private bool Update()
	{
		var state = _stateCache.Get<MusicPlayersStateDto>(Protocol.Callbacks.HostApis.MusicPlayers);
		if (state is null)
		{
			return false;
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var instances = new List<MusicPlayerInstanceSnapshot>(state.Instances.Count);
		foreach (var dto in state.Instances)
		{
			if (dto is null || string.IsNullOrEmpty(dto.Id) || !seen.Add(dto.Id))
			{
				continue;
			}

			instances.Add(MusicPlayerWire.ToSnapshot(dto));
		}

		lock (_gate)
		{
			_instances = instances;
		}

		return true;
	}

	private void Raise()
	{
		Subscription[] subscriptions;
		lock (_gate)
		{
			subscriptions = [.. _subscriptions];
		}

		foreach (var subscription in subscriptions)
		{
			try
			{
				subscription.Handler(this, EventArgs.Empty);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A music player InstancesChanged handler failed");
			}
		}
	}

	private void Subscribe(EventHandler? handler, bool lifecycle)
	{
		if (handler is null)
		{
			return;
		}

		lock (_gate)
		{
			_subscriptions.Add(new Subscription(handler, lifecycle));
		}
	}

	private void Unsubscribe(EventHandler? handler, bool lifecycle)
	{
		if (handler is null)
		{
			return;
		}

		lock (_gate)
		{
			var index = _subscriptions.FindLastIndex(subscription =>
				subscription.Lifecycle == lifecycle && subscription.Handler == handler);
			if (index >= 0)
			{
				_subscriptions.RemoveAt(index);
			}
		}
	}

	private sealed record Subscription(EventHandler Handler, bool Lifecycle);

	private sealed class LifecycleView(RemoteMusicPlayerApi api) : IMusicPlayerApi
	{
		public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances => api.Instances;

		public event EventHandler? InstancesChanged
		{
			add => api.Subscribe(value, true);
			remove => api.Unsubscribe(value, true);
		}

		public Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
		{
			return api.GetAlbumColorAsync(instanceId, cancellationToken);
		}
	}
}
