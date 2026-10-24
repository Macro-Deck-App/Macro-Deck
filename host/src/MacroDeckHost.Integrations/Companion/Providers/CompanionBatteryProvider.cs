using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Companion.Configuration;
using Serilog;

namespace MacroDeckHost.Integrations.Companion.Providers;

internal sealed class CompanionBatteryProvider : IDisposable
{
	private const string FallbackName = "Companion";

	private const string IdPrefix = "companion-battery:";

	private const int IdLength = 16;

	private static readonly ILogger _logger =
		IntegrationLog.For(CompanionIntegration.IntegrationId).ForContext<CompanionBatteryProvider>();

	private readonly Func<ICompanionDeviceApi?> _devicesAccessor;
	private readonly Func<IReadOnlyList<CompanionRuntime>> _runtimesAccessor;
	private readonly Func<IReadOnlySet<Guid>?> _entryIdsAccessor;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private readonly Dictionary<Guid, BatteryDevice> _publishedByEntry = new();

	private ICompanionDeviceApi? _devices;
	private IBatteryProviderContext? _context;

	public CompanionBatteryProvider(
		Func<ICompanionDeviceApi?> devicesAccessor,
		Func<IReadOnlyList<CompanionRuntime>> runtimesAccessor,
		Func<IReadOnlySet<Guid>?> entryIdsAccessor)
	{
		_devicesAccessor = devicesAccessor;
		_runtimesAccessor = runtimesAccessor;
		_entryIdsAccessor = entryIdsAccessor;
	}

	public static string IdFor(Guid entryId)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(IdPrefix + entryId.ToString("N")));
		return Convert.ToHexStringLower(hash)[..IdLength];
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
			devices.DeviceChanged += OnDeviceChanged;
			devices.AccessChanged += OnAccessChanged;
			await ReconcileCoreAsync(false, cancellationToken);
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
			Unsubscribe();
			_context = null;
			_publishedByEntry.Clear();
		}
		finally
		{
			_gate.Release();
		}
	}

	public void EntriesReloaded()
	{
		_ = ReconcileAsync(true);
	}

	public void Dispose()
	{
		Unsubscribe();
		_gate.Dispose();
	}

	private void Unsubscribe()
	{
		if (Interlocked.Exchange(ref _devices, null) is { } devices)
		{
			devices.DeviceChanged -= OnDeviceChanged;
			devices.AccessChanged -= OnAccessChanged;
		}
	}

	private void OnDeviceChanged(object? sender, CompanionDeviceEventArgs e)
	{
		_ = ReconcileAsync(false);
	}

	private void OnAccessChanged(object? sender, EventArgs e)
	{
		_ = ReconcileAsync(false);
	}

	private async Task ReconcileAsync(bool removeDeleted)
	{
		try
		{
			await _gate.WaitAsync();
			try
			{
				await ReconcileCoreAsync(removeDeleted, CancellationToken.None);
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not publish the batteries of Companion devices");
		}
	}

	private async Task ReconcileCoreAsync(bool removeDeleted, CancellationToken cancellationToken)
	{
		if (_context is not { } context || _devices is not { } devices)
		{
			return;
		}

		if (removeDeleted && _entryIdsAccessor() is { } existing)
		{
			foreach (var entryId in _publishedByEntry.Keys.Where(id => !existing.Contains(id)).ToList())
			{
				await context.RemoveAsync(_publishedByEntry[entryId].Id, cancellationToken);
				_publishedByEntry.Remove(entryId);
			}
		}

		var batch = new Dictionary<Guid, BatteryDevice>();
		foreach (var runtime in _runtimesAccessor())
		{
			var state = devices.FindDevice(runtime.Id)?.State;
			_publishedByEntry.TryGetValue(runtime.Id, out var previous);
			if (state is not null && (state.BatteryLevelPercent is not null || previous is not null))
			{
				batch[runtime.Id] = Describe(runtime, state);
			}
			else if (previous is not null)
			{
				batch[runtime.Id] = previous with
				{
					Name = NameOf(runtime.Title, null, previous.Name),
					IsConnected = false
				};
			}
		}

		foreach (var (entryId, published) in _publishedByEntry.Where(pair => !batch.ContainsKey(pair.Key)))
		{
			batch[entryId] = published with { IsConnected = false };
		}

		var changed = batch
			.Where(pair => !_publishedByEntry.TryGetValue(pair.Key, out var published) || published != pair.Value)
			.ToList();
		if (changed.Count == 0)
		{
			return;
		}

		await context.PublishAsync([.. changed.Select(pair => pair.Value)], cancellationToken);
		foreach (var (entryId, device) in changed)
		{
			_publishedByEntry[entryId] = device;
		}
	}

	private static BatteryDevice Describe(CompanionRuntime runtime, CompanionDeviceState state)
	{
		return new BatteryDevice
		{
			Id = IdFor(runtime.Id),
			Name = NameOf(runtime.Title, state.Model, FallbackName),
			Kind = KindOf(state),
			LevelPercent = state.BatteryLevelPercent,
			ChargeState = ChargeStateOf(state),
			IsConnected = true
		};
	}

	private static string NameOf(string? title, string? model, string fallback)
	{
		var name = title?.Trim();
		if (string.IsNullOrEmpty(name))
		{
			name = model?.Trim();
		}

		name = string.IsNullOrEmpty(name) ? fallback : name;
		return name.Length > BatteryLimits.MaxNameLength ? name[..BatteryLimits.MaxNameLength] : name;
	}

	private static string KindOf(CompanionDeviceState state)
	{
		switch (state.FormFactor)
		{
			case CompanionFormFactors.Tablet:
				return BatteryDeviceKinds.Tablet;
			case CompanionFormFactors.Phone:
				return BatteryDeviceKinds.Phone;
		}

		if (StartsWith(state.Model, "iPad") || StartsWith(state.Platform, "iPadOS"))
		{
			return BatteryDeviceKinds.Tablet;
		}

		return StartsWith(state.Model, "iPhone") || StartsWith(state.Platform, "iOS")
			? BatteryDeviceKinds.Phone
			: BatteryDeviceKinds.Other;
	}

	private static bool StartsWith(string? value, string prefix)
	{
		return value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
	}

	private static BatteryChargeState ChargeStateOf(CompanionDeviceState state)
	{
		if (state.BatteryLevelPercent is not { } level)
		{
			return BatteryChargeState.Unknown;
		}

		if (!state.IsCharging)
		{
			return BatteryChargeState.Discharging;
		}

		return level == 100 ? BatteryChargeState.Full : BatteryChargeState.Charging;
	}
}
