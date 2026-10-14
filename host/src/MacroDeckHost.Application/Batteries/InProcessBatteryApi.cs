using MacroDeck.Sdk.Batteries;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Batteries;

public sealed class InProcessBatteryApi : IBatteryApi, IDisposable
{
	private readonly IBatteryRegistry _registry;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<EventHandler<BatteryDevicesChangedEventArgs>> _handlers = [];
	private Task _raising = Task.CompletedTask;
	private bool _disposed;

	public InProcessBatteryApi(IBatteryRegistry registry, ILogger logger)
	{
		_registry = registry;
		_logger = logger.ForContext<InProcessBatteryApi>();
		_registry.Changed += OnChanged;
	}

	public IReadOnlyList<BatteryDeviceSnapshot> Devices => _registry.Devices;

	public BatteryDeviceSnapshot? Find(string id)
	{
		return _registry.Find(id);
	}

	public event EventHandler<BatteryDevicesChangedEventArgs>? Changed
	{
		add
		{
			if (value is null)
			{
				return;
			}

			lock (_gate)
			{
				if (!_disposed)
				{
					_handlers.Add(value);
				}
			}
		}
		remove
		{
			if (value is null)
			{
				return;
			}

			lock (_gate)
			{
				var index = _handlers.LastIndexOf(value);
				if (index >= 0)
				{
					_handlers.RemoveAt(index);
				}
			}
		}
	}

	public void Dispose()
	{
		_registry.Changed -= OnChanged;
		lock (_gate)
		{
			_disposed = true;
			_handlers.Clear();
		}
	}

	private void OnChanged(object? sender, BatteryDevicesChangedEventArgs args)
	{
		lock (_gate)
		{
			if (_disposed || _handlers.Count == 0)
			{
				return;
			}

			_raising = _raising.ContinueWith(_ => Raise(args),
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
	}

	private void Raise(BatteryDevicesChangedEventArgs args)
	{
		EventHandler<BatteryDevicesChangedEventArgs>[] handlers;
		lock (_gate)
		{
			handlers = [.. _handlers];
		}

		foreach (var handler in handlers)
		{
			try
			{
				handler(this, args);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "A battery Changed handler failed");
			}
		}
	}
}
