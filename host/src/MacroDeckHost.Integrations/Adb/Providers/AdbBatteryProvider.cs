using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Adb.Providers;

internal sealed class AdbBatteryProvider : IDisposable
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

	internal const string CharacteristicsCommand = "getprop ro.build.characteristics";

	private const string FallbackName = "Android";

	private static readonly ILogger _logger =
		IntegrationLog.For(AdbIntegration.IntegrationId).ForContext<AdbBatteryProvider>();

	private readonly Func<IAndroidDeviceManager?> _devicesAccessor;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private readonly Dictionary<string, string> _kindById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BatteryDevice> _publishedById = new(StringComparer.Ordinal);

	private IAndroidDeviceManager? _devices;
	private IBatteryProviderContext? _context;
	private CancellationTokenSource? _pollCancellation;
	private Task? _pollTask;

	public AdbBatteryProvider(Func<IAndroidDeviceManager?> devicesAccessor, TimeProvider timeProvider)
	{
		_devicesAccessor = devicesAccessor;
		_timeProvider = timeProvider;
	}

	public async Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken)
	{
		await ShutdownAsync(cancellationToken);

		if (_devicesAccessor() is not { } devices)
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			_context = context;
			_devices = devices;
			Subscribe(devices);
			await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
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
			if (_devices is { } devices)
			{
				Unsubscribe(devices);
			}

			_devices = null;
			_context = null;
			_kindById.Clear();
			_publishedById.Clear();
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		if (_devices is { } devices)
		{
			Unsubscribe(devices);
		}

		_pollCancellation?.Cancel();
		_pollCancellation?.Dispose();
		_pollCancellation = null;
		_gate.Dispose();
	}

	private void Subscribe(IAndroidDeviceManager devices)
	{
		devices.DeviceConnected += OnDevicesChanged;
		devices.DeviceDisconnected += OnDevicesChanged;
		devices.DeviceStateChanged += OnDevicesChanged;
		devices.AccessChanged += OnAccessChanged;
	}

	private void Unsubscribe(IAndroidDeviceManager devices)
	{
		devices.DeviceConnected -= OnDevicesChanged;
		devices.DeviceDisconnected -= OnDevicesChanged;
		devices.DeviceStateChanged -= OnDevicesChanged;
		devices.AccessChanged -= OnAccessChanged;
	}

	private void OnDevicesChanged(object? sender, AndroidDeviceEventArgs e)
	{
		_ = ReconcileAsync();
	}

	private void OnAccessChanged(object? sender, EventArgs e)
	{
		_ = ReconcileAsync();
	}

	private async Task ReconcileAsync()
	{
		try
		{
			await _gate.WaitAsync();
			try
			{
				await ReconcileCoreAsync(CancellationToken.None);
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not publish the batteries of adb devices after a device change");
		}
	}

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(PollInterval, _timeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				await _gate.WaitAsync(cancellationToken);
				try
				{
					await ReconcileCoreAsync(cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_logger.Warning(ex, "Could not refresh the battery levels of adb devices");
				}
				finally
				{
					_gate.Release();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	// Reads the device list instead of trusting events alone: turning ADB off or losing the server empties
	// it, and every phone no longer in it has to be shown as not connected.
	private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context || _devices is not { } devices)
		{
			return;
		}

		var attached = devices.Access == AndroidDeviceAccess.Available ? devices.Devices : [];
		var live = attached
			.Where(device => device is { State: AndroidDeviceState.Online, Info.StableId: not null })
			.GroupBy(device => device.Info.StableId!, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => group.OrderBy(device => device.Serial, StringComparer.Ordinal).First(),
				StringComparer.Ordinal);

		var batch = new List<BatteryDevice>(live.Count + _publishedById.Count);
		foreach (var (stableId, device) in live)
		{
			batch.Add(await ReadAsync(stableId, device, cancellationToken));
		}

		batch.AddRange(_publishedById.Values
			.Where(published => published.IsConnected && !live.ContainsKey(published.Id))
			.Select(published => published with { IsConnected = false }));

		await PublishChangedAsync(context, batch);
	}

	private async Task<BatteryDevice> ReadAsync(
		string stableId,
		IAndroidDevice device,
		CancellationToken cancellationToken)
	{
		var battery = await TryReadBatteryAsync(device, cancellationToken);
		_publishedById.TryGetValue(stableId, out var previous);

		return new BatteryDevice
		{
			Id = stableId,
			Name = NameOf(device.Info),
			Kind = await KindOfAsync(stableId, device, cancellationToken),
			LevelPercent = battery?.Level ?? previous?.LevelPercent,
			ChargeState = battery is null
				? previous?.ChargeState ?? BatteryChargeState.Unknown
				: MapChargeState(battery.Status),
			Health = battery is null ? previous?.Health ?? BatteryHealth.Unknown : MapHealth(battery.Health),
			IsConnected = true
		};
	}

	private static async Task<AndroidBatteryState?> TryReadBatteryAsync(
		IAndroidDevice device,
		CancellationToken cancellationToken)
	{
		try
		{
			return await device.GetBatteryStateAsync(cancellationToken);
		}
		catch (AndroidDeviceException ex)
		{
			_logger.Debug(ex, "Could not read the battery of an adb device ({ErrorCode})", ex.ErrorCode);
			return null;
		}
	}

	// Only an answered probe is remembered: a locked host or a busy device is asked again on the next pass,
	// and the device counts as a phone until then.
	private async Task<string> KindOfAsync(string stableId, IAndroidDevice device, CancellationToken cancellationToken)
	{
		if (_kindById.TryGetValue(stableId, out var known))
		{
			return known;
		}

		try
		{
			var result = await device.ExecuteShellAsync(CharacteristicsCommand, cancellationToken);
			if (result.ExitCode != 0)
			{
				return BatteryDeviceKinds.Phone;
			}

			var kind = IsTablet(result.StandardOutput) ? BatteryDeviceKinds.Tablet : BatteryDeviceKinds.Phone;
			_kindById[stableId] = kind;
			return kind;
		}
		catch (AndroidDeviceException ex)
		{
			_logger.Debug(ex, "Could not tell whether an adb device is a tablet ({ErrorCode})", ex.ErrorCode);
			return BatteryDeviceKinds.Phone;
		}
	}

	private async Task PublishChangedAsync(IBatteryProviderContext context, IReadOnlyList<BatteryDevice> devices)
	{
		var changed = devices
			.Where(device => !_publishedById.TryGetValue(device.Id, out var published) || published != device)
			.ToList();
		if (changed.Count == 0)
		{
			return;
		}

		await context.PublishAsync(changed);
		foreach (var device in changed)
		{
			_publishedById[device.Id] = device;
		}
	}

	private static bool IsTablet(string buildCharacteristics)
	{
		return buildCharacteristics.Split(',', StringSplitOptions.TrimEntries)
			.Contains("tablet", StringComparer.OrdinalIgnoreCase);
	}

	private static string NameOf(AndroidDeviceInfo info)
	{
		// adb devices -l reports the model with underscores in place of spaces.
		var name = info.Model?.Replace('_', ' ').Trim();
		if (string.IsNullOrEmpty(name))
		{
			name = info.Manufacturer?.Trim();
		}

		name = string.IsNullOrEmpty(name) ? FallbackName : name;
		return name.Length > BatteryLimits.MaxNameLength ? name[..BatteryLimits.MaxNameLength] : name;
	}

	private static BatteryChargeState MapChargeState(AndroidBatteryStatus status)
	{
		return status switch
		{
			AndroidBatteryStatus.Charging => BatteryChargeState.Charging,
			AndroidBatteryStatus.Discharging => BatteryChargeState.Discharging,
			AndroidBatteryStatus.NotCharging => BatteryChargeState.NotCharging,
			AndroidBatteryStatus.Full => BatteryChargeState.Full,
			_ => BatteryChargeState.Unknown
		};
	}

	private static BatteryHealth MapHealth(AndroidBatteryHealth health)
	{
		return health switch
		{
			AndroidBatteryHealth.Good => BatteryHealth.Good,
			AndroidBatteryHealth.Overheat => BatteryHealth.Overheat,
			AndroidBatteryHealth.Dead => BatteryHealth.Dead,
			AndroidBatteryHealth.OverVoltage => BatteryHealth.OverVoltage,
			AndroidBatteryHealth.Cold => BatteryHealth.Cold,
			AndroidBatteryHealth.Failure => BatteryHealth.Failure,
			_ => BatteryHealth.Unknown
		};
	}
}
