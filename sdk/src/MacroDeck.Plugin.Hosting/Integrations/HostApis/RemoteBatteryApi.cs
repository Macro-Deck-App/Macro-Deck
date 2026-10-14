using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Batteries;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteBatteryApi : IBatteryApi, IDisposable
{
	private readonly HostStateCache _stateCache;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<Subscription> _subscriptions = [];
	private IReadOnlyList<BatteryDeviceSnapshot> _devices = [];
	private Dictionary<string, BatteryDeviceSnapshot> _byId = new(StringComparer.Ordinal);
	private Task _raising = Task.CompletedTask;

	public RemoteBatteryApi(HostStateCache stateCache, ILogger logger)
	{
		_stateCache = stateCache;
		_logger = logger.ForContext<RemoteBatteryApi>();
		Lifecycle = new LifecycleView(this);
		_stateCache.BatteriesChanged += OnBatteriesChanged;
		Update();
	}

	public IBatteryApi Lifecycle { get; }

	public IReadOnlyList<BatteryDeviceSnapshot> Devices
	{
		get
		{
			lock (_gate)
			{
				return _devices;
			}
		}
	}

	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed
	{
		add => Subscribe(value, false);
		remove => Unsubscribe(value, false);
	}

	public BatteryDeviceSnapshot? Find(string id)
	{
		ArgumentNullException.ThrowIfNull(id);

		lock (_gate)
		{
			return _byId.GetValueOrDefault(id);
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
		_stateCache.BatteriesChanged -= OnBatteriesChanged;
	}

	private void OnBatteriesChanged()
	{
		var removedIds = Update();
		if (removedIds is null)
		{
			return;
		}

		var args = new BatteryDevicesChangedEventArgs(removedIds);

		// Raised off the receive loop and in push order, so a slow or throwing handler cannot stall the connection.
		lock (_gate)
		{
			_raising = _raising.ContinueWith(_ => Raise(args),
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private IReadOnlyList<string>? Update()
	{
		var state = _stateCache.Get<BatteryStateDto>(Protocol.Callbacks.HostApis.Batteries);
		if (state is null)
		{
			return null;
		}

		var devices = new List<BatteryDeviceSnapshot>(state.Devices.Count);
		var byId = new Dictionary<string, BatteryDeviceSnapshot>(StringComparer.Ordinal);
		foreach (var dto in state.Devices)
		{
			if (dto is null || string.IsNullOrEmpty(dto.Id) || byId.ContainsKey(dto.Id))
			{
				continue;
			}

			var snapshot = BatteryWire.ToSnapshot(dto);
			byId[snapshot.Id] = snapshot;
			devices.Add(snapshot);
		}

		lock (_gate)
		{
			_devices = devices;
			_byId = byId;
		}

		return [.. state.RemovedIds.Where(id => !string.IsNullOrEmpty(id))];
	}

	private void Raise(BatteryDevicesChangedEventArgs args)
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
				subscription.Handler(this, args);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A battery Changed handler failed");
			}
		}
	}

	private void Subscribe(EventHandler<BatteryDevicesChangedEventArgs>? handler, bool lifecycle)
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

	private void Unsubscribe(EventHandler<BatteryDevicesChangedEventArgs>? handler, bool lifecycle)
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

	private sealed record Subscription(EventHandler<BatteryDevicesChangedEventArgs> Handler, bool Lifecycle);

	private sealed class LifecycleView(RemoteBatteryApi api) : IBatteryApi
	{
		public IReadOnlyList<BatteryDeviceSnapshot> Devices => api.Devices;

		public BatteryDeviceSnapshot? Find(string id)
		{
			return api.Find(id);
		}

		public event EventHandler<BatteryDevicesChangedEventArgs>? Changed
		{
			add => api.Subscribe(value, true);
			remove => api.Unsubscribe(value, true);
		}
	}
}
