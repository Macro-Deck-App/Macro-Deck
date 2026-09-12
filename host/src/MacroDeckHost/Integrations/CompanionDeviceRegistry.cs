using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Companion;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations;

public sealed class CompanionDeviceRegistry : ICompanionGateway
{
	private const string IntegrationId = CompanionIntegration.IntegrationId;
	private const int MaximumTextLength = 64;

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Func<IIntegrationConfigMutationCoordinator> _coordinator;
	private readonly IUiTransport _transport;
	private readonly IIntegrationRegistry _registry;
	private readonly ILogger _logger;
	private readonly Lock _connectionGate = new();
	private readonly Dictionary<string, Guid> _connections = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, CompanionDeviceState> _states = new();
	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _deviceGates = new();
	private readonly ConcurrentDictionary<Guid, Task> _creations = new();
	private readonly ConcurrentDictionary<Guid, bool> _failedCreations = new();

	public CompanionDeviceRegistry(
		IServiceScopeFactory scopeFactory,
		Func<IIntegrationConfigMutationCoordinator> coordinator,
		IUiTransport transport,
		IIntegrationRegistry registry,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_coordinator = coordinator;
		_transport = transport;
		_registry = registry;
		_logger = logger;
		_registry.AvailabilityChanged += OnAvailabilityChanged;
	}

	public event EventHandler<Guid>? StateChanged;

	public void Report(string connectionId, Guid deviceId, ReportCompanionStateRequest report)
	{
		bool firstOfConnection;
		lock (_connectionGate)
		{
			firstOfConnection = _connections.TryAdd(connectionId, deviceId);
			_states[deviceId] = Sanitize(report);
		}

		StateChanged?.Invoke(this, deviceId);
		if (firstOfConnection || _failedCreations.TryRemove(deviceId, out _))
		{
			_creations[deviceId] = Task.Run(() => EnsureConfigurationAsync(deviceId));
		}
	}

	public void Disconnected(string connectionId)
	{
		Guid deviceId;
		lock (_connectionGate)
		{
			if (!_connections.Remove(connectionId, out deviceId) || _connections.ContainsValue(deviceId))
			{
				return;
			}

			_states.TryRemove(deviceId, out _);
		}

		_creations.TryRemove(deviceId, out _);
		StateChanged?.Invoke(this, deviceId);
	}

	public bool TryGetState(Guid deviceId, out CompanionDeviceState state)
		=> _states.TryGetValue(deviceId, out state!);

	public async Task<bool> SendAsync(Guid deviceId, CompanionCommand command, CancellationToken cancellationToken)
	{
		if (!_states.ContainsKey(deviceId))
		{
			return false;
		}

		await _transport.SendToGroup(UiDeviceGroups.For(deviceId),
			new CompanionCommandEvent
			{
				Command = command.Command,
				BrightnessPercent = command.BrightnessPercent,
				Orientation = command.Orientation
			},
			cancellationToken);
		return true;
	}

	public async Task<bool> ResumeAutoCreationAsync(CancellationToken cancellationToken)
	{
		var entries = await _coordinator().DescribeAsync(IntegrationId, cancellationToken);
		if (entries.Any(entry => entry.Usable) || !_registry.ClearDisabledChoice(IntegrationId))
		{
			return false;
		}

		ScheduleCreationForConnectedDevices();
		return true;
	}

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs change)
	{
		if (change.IsAvailable && string.Equals(change.IntegrationId, IntegrationId, StringComparison.Ordinal))
		{
			ScheduleCreationForConnectedDevices();
		}
	}

	private void ScheduleCreationForConnectedDevices()
	{
		List<Guid> connected;
		lock (_connectionGate)
		{
			connected = _connections.Values.Distinct().ToList();
		}

		foreach (var deviceId in connected)
		{
			_creations[deviceId] = Task.Run(() => EnsureConfigurationAsync(deviceId), CancellationToken.None);
		}
	}

	public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
	{
		var gate = GateFor(deviceId);
		await gate.WaitAsync(cancellationToken);
		try
		{
			// Forgotten before the delete: another device created meanwhile switches the integration on,
			// which schedules creation for every connected device and must not include this one.
			bool wasConnected;
			lock (_connectionGate)
			{
				foreach (var connectionId in _connections.Where(pair => pair.Value == deviceId)
					.Select(pair => pair.Key)
					.ToList())
				{
					_connections.Remove(connectionId);
				}

				wasConnected = _states.TryRemove(deviceId, out _);
			}

			_creations.TryRemove(deviceId, out _);
			if (wasConnected)
			{
				StateChanged?.Invoke(this, deviceId);
			}

			if (await FindEntryAsync(deviceId) is not null)
			{
				await _coordinator().DeleteWithoutStoringDisabledAsync(IntegrationId, deviceId, cancellationToken);
			}
		}
		finally
		{
			gate.Release();
		}
	}

	internal Task CreationFor(Guid deviceId) => _creations.GetValueOrDefault(deviceId) ?? Task.CompletedTask;

	private async Task EnsureConfigurationAsync(Guid deviceId)
	{
		var gate = GateFor(deviceId);
		await gate.WaitAsync();
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var device = await scope.ServiceProvider.GetRequiredService<IDeviceRepository>().GetById(deviceId);
			if (device is null ||
				device.ClientType != DeviceClientType.Native ||
				_registry.IsExplicitlyDisabled(IntegrationId))
			{
				return;
			}

			var coordinator = _coordinator();
			var existing = await scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>().Find(deviceId);
			if (existing is not null &&
				(await coordinator.DescribeAsync(IntegrationId, CancellationToken.None))
				.Any(entry => entry.Id == deviceId && entry.Usable))
			{
				return;
			}

			var disabledVersion = _registry.DisabledVersion(IntegrationId);
			if (_registry.IsExplicitlyDisabled(IntegrationId))
			{
				return;
			}

			var outcome = await coordinator.CompleteUnlessDisabledAsync(IntegrationId,
				deviceId,
				existing?.Title ?? device.Name,
				new Dictionary<string, JsonElement>(),
				CancellationToken.None);
			if (!outcome.Success)
			{
				if (!_registry.IsExplicitlyDisabled(IntegrationId))
				{
					_failedCreations[deviceId] = true;
					_logger.Warning("Could not create the Companion configuration for device {DeviceId}", deviceId);
				}

				return;
			}

			// CompleteAsync switches the integration on; a user who switched it off meanwhile keeps that choice.
			if (_registry.DisabledVersion(IntegrationId) != disabledVersion)
			{
				await scope.ServiceProvider
					.GetRequiredService<IUiTransportMessageHandler<SetIntegrationEnabledRequest,
						SetIntegrationEnabledResponse>>()
					.Handle(new SetIntegrationEnabledRequest { Id = IntegrationId, Enabled = false },
						CancellationToken.None);
			}
		}
		catch (Exception ex)
		{
			_failedCreations[deviceId] = true;
			_logger.Error(ex, "Creating the Companion configuration for device {DeviceId} failed", deviceId);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<ConfigEntryRecord?> FindEntryAsync(Guid deviceId)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var entry = await scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>().Find(deviceId);
		return entry is not null && string.Equals(entry.IntegrationId, IntegrationId, StringComparison.Ordinal)
			? entry
			: null;
	}

	private SemaphoreSlim GateFor(Guid deviceId) => _deviceGates.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));

	private static CompanionDeviceState Sanitize(ReportCompanionStateRequest report)
		=> new(Percent(report.BatteryLevelPercent),
			report.Charging,
			report.Orientation is "portrait" or "landscape" ? report.Orientation : null,
			Percent(report.ScreenBrightnessPercent),
			Text(report.Model),
			Text(report.Platform),
			Text(report.AppVersion));

	private static int? Percent(int? value) => value is { } percent ? Math.Clamp(percent, 0, 100) : null;

	private static string? Text(string? value)
		=> value is { Length: > MaximumTextLength } ? value[..MaximumTextLength] : value;
}
