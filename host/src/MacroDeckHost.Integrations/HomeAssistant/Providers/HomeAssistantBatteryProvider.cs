using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.HomeAssistant.Connections;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Integrations.HomeAssistant.State;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant.Providers;

internal sealed class HomeAssistantBatteryProvider : IDisposable
{
	private const string IdPrefix = "ha-";

	private const string HashPrefix = "home-assistant-battery:";

	private const int IdLength = 16;

	private const string ChargingDeviceClass = "battery_charging";

	private const string BinarySensorDomain = "binary_sensor";

	private static readonly ILogger _logger =
		IntegrationLog.For(HomeAssistantIntegration.IntegrationId).ForContext<HomeAssistantBatteryProvider>();

	private readonly Func<HomeAssistantConnection?> _connectionAccessor;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private readonly Dictionary<string, BatteryDevice> _publishedByEntity = new(StringComparer.Ordinal);

	private IReadOnlyList<string> _selection = [];
	private IBatteryProviderContext? _context;
	private int _generation;
	private int _reconcileQueued;

	public HomeAssistantBatteryProvider(Func<HomeAssistantConnection?> connectionAccessor)
	{
		_connectionAccessor = connectionAccessor;
	}

	// Every plugin may read the catalog, so the entity id is not spelled out; the hash is unkeyed and guessable.
	public static string IdFor(string entityId)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(HashPrefix + entityId));
		return IdPrefix + Convert.ToHexStringLower(hash)[..IdLength];
	}

	public static bool IsBatterySensor(HomeAssistantEntityState state)
	{
		return string.Equals(state.Domain, "sensor", StringComparison.Ordinal)
			&& string.Equals(state.ReadAttribute("device_class"), "battery", StringComparison.Ordinal);
	}

	// Every new connection gets a new generation, so a callback from a replaced or disposed connection
	// that is still unwinding cannot reconcile against the connection that took its place.
	public int BeginConnection()
	{
		return Interlocked.Increment(ref _generation);
	}

	public void ConnectionChanged(int generation)
	{
		if (generation != Volatile.Read(ref _generation)
			|| Interlocked.Exchange(ref _reconcileQueued, 1) == 1)
		{
			return;
		}

		_ = Task.Run(ReconcileQueuedAsync);
	}

	public async Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken)
	{
		await ShutdownAsync(cancellationToken);

		await _gate.WaitAsync(cancellationToken);
		try
		{
			_context = context;
			await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task ShutdownAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_context = null;
			_publishedByEntity.Clear();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task UpdateSelectionAsync(IReadOnlyList<string> selection, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_selection = [.. selection.Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)];
			if (_context is not { } context)
			{
				return;
			}

			var kept = _selection.ToHashSet(StringComparer.Ordinal);
			foreach (var entityId in _publishedByEntity.Keys.Where(id => !kept.Contains(id)).ToList())
			{
				await context.RemoveAsync(_publishedByEntity[entityId].Id, cancellationToken);
				_publishedByEntity.Remove(entityId);
			}

			await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_context = null;
		_gate.Dispose();
	}

	private async Task ReconcileQueuedAsync()
	{
		try
		{
			await _gate.WaitAsync();
			try
			{
				Interlocked.Exchange(ref _reconcileQueued, 0);
				await ReconcileCoreAsync(CancellationToken.None);
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not publish the Home Assistant battery sensors");
		}
	}

	private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
	{
		if (_context is not { } context)
		{
			return;
		}

		var connection = _connectionAccessor();
		var connected = connection is { IsConnected: true };
		var catalog = connection?.Catalog ?? HomeAssistantCatalog.Empty;

		var selection = _selection
			.OrderBy(entityId => _publishedByEntity.ContainsKey(entityId) ? 0 : 1)
			.Take(BatteryLimits.MaxDevicesPerProvider)
			.ToList();
		var chargingByDevice = FindChargingSensors(connection, catalog, selection);
		var sensorsPerDevice = selection
			.Select(entityId => catalog.EntityDevices.GetValueOrDefault(entityId))
			.OfType<string>()
			.GroupBy(deviceId => deviceId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

		var batch = new List<(string EntityId, BatteryDevice Device)>(selection.Count);
		foreach (var entityId in selection)
		{
			_publishedByEntity.TryGetValue(entityId, out var previous);
			var state = connected ? connection!.Entity(entityId) : null;
			if (state is null)
			{
				if (previous is not null)
				{
					batch.Add((entityId, previous with { IsConnected = false }));
				}

				continue;
			}

			var deviceId = catalog.EntityDevices.GetValueOrDefault(entityId);
			var name = NameOf(state, deviceId, catalog, sensorsPerDevice);
			if (state.State is "unavailable" or "unknown")
			{
				batch.Add((entityId, new BatteryDevice
				{
					Id = IdFor(entityId),
					Name = name,
					Kind = BatteryDeviceKinds.Other,
					LevelPercent = previous?.LevelPercent,
					ChargeState = previous?.ChargeState ?? BatteryChargeState.Unknown,
					IsConnected = false
				}));
				continue;
			}

			batch.Add((entityId, new BatteryDevice
			{
				Id = IdFor(entityId),
				Name = name,
				Kind = BatteryDeviceKinds.Other,
				LevelPercent = LevelOf(state.State),
				ChargeState = deviceId is not null && chargingByDevice.TryGetValue(deviceId, out var charging)
					? ChargeStateOf(connection!.Entity(charging))
					: BatteryChargeState.Unknown,
				IsConnected = true
			}));
		}

		var relevant = new HashSet<string>(selection, StringComparer.Ordinal);
		relevant.UnionWith(chargingByDevice.Values);
		connection?.WatchBatteryEntities(relevant);

		var changed = batch
			.Where(pair => !_publishedByEntity.TryGetValue(pair.EntityId, out var published)
				|| published != pair.Device)
			.ToList();
		if (changed.Count == 0)
		{
			return;
		}

		await context.PublishAsync([.. changed.Select(pair => pair.Device)], cancellationToken);
		foreach (var (entityId, device) in changed)
		{
			_publishedByEntity[entityId] = device;
		}
	}

	private static Dictionary<string, string> FindChargingSensors(
		HomeAssistantConnection? connection,
		HomeAssistantCatalog catalog,
		IReadOnlyList<string> selection)
	{
		var devices = selection
			.Select(entityId => catalog.EntityDevices.GetValueOrDefault(entityId))
			.OfType<string>()
			.ToHashSet(StringComparer.Ordinal);
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		if (connection is null || devices.Count == 0)
		{
			return result;
		}

		foreach (var (entityId, deviceId) in catalog.EntityDevices)
		{
			if (devices.Contains(deviceId)
				&& !result.ContainsKey(deviceId)
				&& string.Equals(HomeAssistantEntityState.DomainOf(entityId),
					BinarySensorDomain,
					StringComparison.Ordinal)
				&& string.Equals(connection.Entity(entityId)?.ReadAttribute("device_class"),
					ChargingDeviceClass,
					StringComparison.Ordinal))
			{
				result[deviceId] = entityId;
			}
		}

		return result;
	}

	private static string NameOf(
		HomeAssistantEntityState state,
		string? deviceId,
		HomeAssistantCatalog catalog,
		IReadOnlyDictionary<string, int> sensorsPerDevice)
	{
		var deviceName = deviceId is not null && sensorsPerDevice.GetValueOrDefault(deviceId) == 1
			? catalog.Devices.FirstOrDefault(device => device.DeviceId == deviceId)?.Name.Trim()
			: null;
		var name = deviceName is { Length: > 0 } ? deviceName : state.FriendlyName?.Trim();
		name = string.IsNullOrEmpty(name) ? state.EntityId : name;
		return name.Length > BatteryLimits.MaxNameLength ? name[..BatteryLimits.MaxNameLength] : name;
	}

	private static int? LevelOf(string state)
	{
		return double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
			&& level is >= 0 and <= 100
				? (int)Math.Round(level, MidpointRounding.AwayFromZero)
				: null;
	}

	// Home Assistant only says whether power flows in, so a plugged-in full battery reads as discharging.
	private static BatteryChargeState ChargeStateOf(HomeAssistantEntityState? charging)
	{
		return charging?.State switch
		{
			"on" => BatteryChargeState.Charging,
			"off" => BatteryChargeState.Discharging,
			_ => BatteryChargeState.Unknown
		};
	}
}
