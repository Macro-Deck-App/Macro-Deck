using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.System.Providers;

internal sealed class SystemBatteryProvider : IDisposable
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

	private const string IdPrefix = "internal-";

	private static readonly ILogger _logger =
		IntegrationLog.For(SystemIntegration.IntegrationId).ForContext<SystemBatteryProvider>();

	private readonly IHostBatteryReader _reader;
	private readonly TimeProvider _timeProvider;
	private readonly string _machineName;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private readonly Dictionary<string, BatteryDevice> _publishedById = new(StringComparer.Ordinal);

	private IBatteryProviderContext? _context;
	private CancellationTokenSource? _pollCancellation;
	private Task? _pollTask;

	public SystemBatteryProvider(IHostBatteryReader reader, TimeProvider timeProvider, string machineName)
	{
		_reader = reader;
		_timeProvider = timeProvider;
		_machineName = machineName.Trim();
	}

	public async Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken)
	{
		await ShutdownAsync(cancellationToken);

		bool keepPolling;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_context = context;
			keepPolling = await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}

		if (!keepPolling)
		{
			return;
		}

		_pollCancellation = new CancellationTokenSource();
		_pollTask = PollAsync(_pollCancellation.Token);
	}

	public async Task ShutdownAsync(CancellationToken cancellationToken)
	{
		if (_pollCancellation is { } pollCancellation)
		{
			await pollCancellation.CancelAsync();
			await (_pollTask ?? Task.CompletedTask);
			pollCancellation.Dispose();
			_pollCancellation = null;
			_pollTask = null;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			_context = null;
			_publishedById.Clear();
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_pollCancellation?.Cancel();
		// The poll loop may still hold the gate, and releasing a disposed gate throws.
		try
		{
			_pollTask?.Wait(TimeSpan.FromSeconds(1));
		}
		catch (Exception ex) when (ex is OperationCanceledException or AggregateException)
		{
		}

		_pollCancellation?.Dispose();
		_pollCancellation = null;
		_pollTask = null;
		_gate.Dispose();
	}

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(PollInterval, _timeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				var keepPolling = true;
				await _gate.WaitAsync(cancellationToken);
				try
				{
					keepPolling = await ReconcileCoreAsync(cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.Warning(ex, "Could not refresh the battery level of this computer");
				}
				finally
				{
					_gate.Release();
				}

				if (!keepPolling)
				{
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	// Returns false once a computer that never reported a battery reads none, so a desktop stops polling.
	private async Task<bool> ReconcileCoreAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context || await TryReadAsync(cancellationToken) is not { } readings)
		{
			return true;
		}

		if (readings.Count == 0 && _publishedById.Count == 0)
		{
			return false;
		}

		var live = readings
			.Take(BatteryLimits.MaxDevicesPerProvider)
			.Select(reading => DeviceOf(reading, readings.Count > 1))
			.ToList();
		var liveIds = live.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
		var batch = live
			.Concat(_publishedById.Values
				.Where(published => published.IsConnected && !liveIds.Contains(published.Id))
				.Select(published => published with { IsConnected = false }))
			.ToList();

		var changed = batch
			.Where(device => !_publishedById.TryGetValue(device.Id, out var published) || published != device)
			.ToList();
		if (changed.Count == 0)
		{
			return true;
		}

		await context.PublishAsync(changed, cancellationToken);
		foreach (var device in changed)
		{
			_publishedById[device.Id] = device;
		}

		return true;
	}

	private async Task<IReadOnlyList<HostBatteryReading>?> TryReadAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _reader.ReadAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Could not read the battery of this computer");
			return null;
		}
	}

	private BatteryDevice DeviceOf(HostBatteryReading reading, bool severalBatteries)
	{
		var id = IdPrefix + reading.Key;
		return new BatteryDevice
		{
			Id = id.Length > BatteryLimits.MaxIdLength ? id[..BatteryLimits.MaxIdLength] : id,
			Name = NameOf(reading.Key, severalBatteries, id),
			Kind = BatteryDeviceKinds.Laptop,
			LevelPercent = reading.LevelPercent,
			ChargeState = reading.ChargeState,
			TimeRemaining = reading.TimeRemaining,
			TimeToFull = reading.TimeToFull,
			IsConnected = true
		};
	}

	private string NameOf(string key, bool severalBatteries, string fallback)
	{
		var name = _machineName.Length == 0 ? fallback : _machineName;
		if (severalBatteries)
		{
			name = $"{name} ({key})";
		}

		return name.Length > BatteryLimits.MaxNameLength ? name[..BatteryLimits.MaxNameLength] : name;
	}
}
