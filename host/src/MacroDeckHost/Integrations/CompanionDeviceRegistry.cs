using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations;

public sealed class CompanionDeviceRegistry : ICompanionDeviceApi
{
	private const int MaximumTextLength = 64;

	private static readonly HashSet<string> _knownCapabilities = new(StringComparer.Ordinal)
	{
		CompanionDeviceCapabilities.ScreenOn,
		CompanionDeviceCapabilities.ScreenOff,
		CompanionDeviceCapabilities.ScreenshotDeck,
		CompanionDeviceCapabilities.ScreenshotFull,
		CompanionDeviceCapabilities.Focus,
		CompanionDeviceCapabilities.NetworkName,
		CompanionDeviceCapabilities.Cpu,
		CompanionDeviceCapabilities.Memory
	};

	private static readonly HashSet<string> _requestableCapabilities = new(StringComparer.Ordinal)
	{
		CompanionDeviceCapabilities.ScreenOn,
		CompanionDeviceCapabilities.ScreenOff,
		CompanionDeviceCapabilities.Focus,
		CompanionDeviceCapabilities.ScreenshotFull
	};

	internal static readonly TimeSpan StateReportTimeout = TimeSpan.FromSeconds(5);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Func<IIntegrationConfigMutationCoordinator> _coordinator;
	private readonly IUiTransport _transport;
	private readonly IIntegrationRegistry _registry;
	private readonly BuiltInIntegrationRegistration _builtInRegistration;
	private readonly IPluginCompanionDeviceAccessPolicy? _pluginAccess;
	private readonly CompanionCommandRequests _requests;
	private readonly INetworkInterfaceSnapshotProvider _interfaces;
	private readonly IHostNameProvider _hostNames;
	private readonly DeviceConnectionTracker _deviceConnections;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _connectionGate = new();
	private readonly Dictionary<string, Guid> _connections = new(StringComparer.Ordinal);
	private readonly HashSet<string> _unreadFormFactors = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, CompanionDevice> _states = new();
	private long _nextReadySequence;
	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _deviceGates = new();
	private readonly ConcurrentDictionary<Guid, Task> _creations = new();
	private readonly ConcurrentDictionary<Guid, bool> _failedCreations = new();
	private readonly Lock _ownerGate = new();
	private (string Id, bool IsPlugin)? _owner;

	public CompanionDeviceRegistry(
		IServiceScopeFactory scopeFactory,
		Func<IIntegrationConfigMutationCoordinator> coordinator,
		IUiTransport transport,
		IIntegrationRegistry registry,
		BuiltInIntegrationRegistration builtInRegistration,
		CompanionCommandRequests requests,
		INetworkInterfaceSnapshotProvider interfaces,
		IHostNameProvider hostNames,
		DeviceConnectionTracker deviceConnections,
		TimeProvider timeProvider,
		ILogger logger,
		IPluginCompanionDeviceAccessPolicy? pluginAccess = null)
	{
		_pluginAccess = pluginAccess;
		_hostNames = hostNames;
		_deviceConnections = deviceConnections;
		_timeProvider = timeProvider;
		_scopeFactory = scopeFactory;
		_coordinator = coordinator;
		_transport = transport;
		_registry = registry;
		_builtInRegistration = builtInRegistration;
		_requests = requests;
		_interfaces = interfaces;
		_logger = logger;
		_registry.AvailabilityChanged += OnAvailabilityChanged;
		_ = _builtInRegistration.Completed.ContinueWith(_ => OnBuiltInRegistrationCompleted(),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
		if (_pluginAccess is not null)
		{
			_pluginAccess.Changed += OnPluginAccessChanged;
		}
	}

	public event EventHandler<CompanionDeviceEventArgs>? DeviceChanged;

	public event EventHandler<CompanionDeviceEventArgs>? DeviceReady;

	public event EventHandler? AccessChanged
	{
		add { }
		remove { }
	}

	private event Action? ConnectionClosed;

	public CompanionDeviceAccess Access => CompanionDeviceAccess.Available;

	public IReadOnlyList<CompanionDevice> Devices => [.. _states.Values];

	public CompanionDevice? FindDevice(Guid deviceId)
	{
		return _states.GetValueOrDefault(deviceId);
	}

	public async Task ReportAsync(string connectionId, Guid deviceId, ReportCompanionStateRequest report)
	{
		bool resolved;
		lock (_connectionGate)
		{
			resolved = _connections.ContainsKey(connectionId) && !_unreadFormFactors.Contains(connectionId);
		}

		var (read, formFactor) = resolved ? (true, null) : await ReadFormFactorAsync(deviceId);

		bool firstOfConnection;
		bool ready;
		CompanionDevice device;
		lock (_connectionGate)
		{
			firstOfConnection = _connections.TryAdd(connectionId, deviceId);
			ready = !_states.TryGetValue(deviceId, out var previous);
			if (resolved)
			{
				formFactor = previous?.State.FormFactor;
			}
			else if (read)
			{
				_unreadFormFactors.Remove(connectionId);
			}
			else
			{
				_unreadFormFactors.Add(connectionId);
			}

			device = new CompanionDevice(deviceId,
				previous?.ReadySequence ?? ++_nextReadySequence,
				Sanitize(report, formFactor));
			_states[deviceId] = device;
		}

		Raise(DeviceChanged, deviceId, device);
		if (ready)
		{
			Raise(DeviceReady, deviceId, device);
		}

		if (firstOfConnection)
		{
			// Off the dispatcher's path: reading the network adapters can block.
			WakeOnLanSent = Task.Run(() => SendWakeOnLanAsync(connectionId));
		}

		if (firstOfConnection || _failedCreations.TryRemove(deviceId, out _))
		{
			_creations[deviceId] = Task.Run(() => EnsureConfigurationAsync(deviceId));
		}
	}

	private async Task SendWakeOnLanAsync(string connectionId)
	{
		try
		{
			await _transport.SendToConnection(connectionId,
				new CompanionWakeOnLanEvent
				{
					InstanceName = _hostNames.MachineName,
					MacAddresses = [.. WakeOnLanPlanner.MacAddresses(_interfaces.GetInterfaces())]
				});
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not send the Wake-on-LAN addresses to connection {ConnectionId}", connectionId);
		}
	}

	public Task SendLicenseAsync(CompanionLicenseEvent license, string? submittingConnectionId)
	{
		return SendToCompanionsAsync(license, submittingConnectionId);
	}

	public Task SendLicenseRevokedAsync(CompanionLicenseRevokedEvent revoked)
	{
		return SendToCompanionsAsync(revoked, null);
	}

	private async Task SendToCompanionsAsync<T>(T message, string? submittingConnectionId)
		where T : class
	{
		List<string> targets;
		lock (_connectionGate)
		{
			targets = [.. _connections.Keys];
		}

		if (submittingConnectionId is not null && !targets.Contains(submittingConnectionId))
		{
			targets.Add(submittingConnectionId);
		}

		foreach (var connectionId in targets)
		{
			try
			{
				await _transport.SendToConnection(connectionId, message);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex,
					"Could not send {Message} to connection {ConnectionId}",
					typeof(T).Name,
					connectionId);
			}
		}
	}

	public void Disconnected(string connectionId)
	{
		try
		{
			Guid deviceId;
			lock (_connectionGate)
			{
				_unreadFormFactors.Remove(connectionId);
				if (!_connections.Remove(connectionId, out deviceId) || _connections.ContainsValue(deviceId))
				{
					return;
				}

				_states.TryRemove(deviceId, out _);
			}

			_creations.TryRemove(deviceId, out _);
			_requests.FailDevice(deviceId, CompanionDeviceFailure.NotConnected);
			Raise(DeviceChanged, deviceId, null);
		}
		finally
		{
			// The dispatcher drops the connection from DeviceConnectionTracker before calling this,
			// so a waiter woken here already sees a socket that never reported as gone.
			ConnectionClosed?.Invoke();
		}
	}

	public bool TryGetState(Guid deviceId, out CompanionDeviceState state)
	{
		var found = _states.TryGetValue(deviceId, out var device);
		state = device?.State!;
		return found;
	}

	public async Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		if (_states.ContainsKey(deviceId))
		{
			return true;
		}

		var signal = Channel.CreateUnbounded<bool>();

		void OnDeviceChanged(object? sender, CompanionDeviceEventArgs changed)
		{
			if (changed.DeviceId == deviceId)
			{
				signal.Writer.TryWrite(true);
			}
		}

		void OnConnectionClosed()
		{
			signal.Writer.TryWrite(true);
		}

		DeviceChanged += OnDeviceChanged;
		ConnectionClosed += OnConnectionClosed;
		try
		{
			using var timeout = new CancellationTokenSource(StateReportTimeout, _timeProvider);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			while (!_states.ContainsKey(deviceId) && _deviceConnections.HasConnection(deviceId))
			{
				try
				{
					await signal.Reader.ReadAsync(linked.Token);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}
		}
		finally
		{
			DeviceChanged -= OnDeviceChanged;
			ConnectionClosed -= OnConnectionClosed;
		}

		return _states.ContainsKey(deviceId);
	}

	public IReadOnlyList<KeyValuePair<Guid, CompanionDeviceState>> ConnectedStates()
	{
		return [.. _states.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.State))];
	}

	public Task<bool> SendAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		return SendWireAsync(deviceId, ToWire(command), cancellationToken);
	}

	public async Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		var result = await RequestWireAsync(deviceId,
			ToWire(command),
			CompanionRequestKind.Command,
			cancellationToken);
		return result.Failure is { } failure
			? CompanionDeviceCommandResult.Failed(failure)
			: result.IsUnconfirmed
				? CompanionDeviceCommandResult.Unconfirmed
				: CompanionDeviceCommandResult.Done;
	}

	public async Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default)
	{
		var result = await RequestWireAsync(deviceId,
			new CompanionCommandEvent
			{
				Command = ScreenshotCommand,
				ScreenshotMode = mode == CompanionScreenshotMode.FullScreen ? "full" : "deck"
			},
			CompanionRequestKind.Screenshot,
			cancellationToken);
		return result.Png is { } png
			? CompanionScreenshotResult.Of(png)
			: CompanionScreenshotResult.Failed(result.Failure ?? CompanionDeviceFailure.Failed);
	}

	private const string ScreenshotCommand = "screenshot";
	private const string CancelRequestCommand = "cancelRequest";

	private async Task<bool> SendWireAsync(
		Guid deviceId,
		CompanionCommandEvent command,
		CancellationToken cancellationToken)
	{
		if (!_states.ContainsKey(deviceId))
		{
			return false;
		}

		await _transport.SendToGroup(UiDeviceGroups.For(deviceId), command, cancellationToken);
		return true;
	}

	private async Task<CompanionCommandResult> RequestWireAsync(
		Guid deviceId,
		CompanionCommandEvent command,
		CompanionRequestKind kind,
		CancellationToken cancellationToken)
	{
		var result = await _requests.RequestAsync(deviceId,
			kind,
			_states.TryGetValue(deviceId, out var device) && device.State.ConfirmsCommands,
			requestId => SendWireAsync(deviceId, With(command, requestId), cancellationToken),
			requestId => _ = CancelOnDeviceAsync(deviceId, requestId),
			cancellationToken);
		if (result.IsUnconfirmed)
		{
			_logger.Information("Companion device {DeviceId} claimed {Command} but never confirmed it",
				deviceId,
				command.Command);
		}

		return result;
	}

	private static CompanionCommandEvent With(CompanionCommandEvent command, string requestId)
	{
		return new CompanionCommandEvent
		{
			Command = command.Command,
			BrightnessPercent = command.BrightnessPercent,
			Orientation = command.Orientation,
			RequestId = requestId,
			ScreenshotMode = command.ScreenshotMode
		};
	}

	private static CompanionCommandEvent ToWire(CompanionDeviceCommand command)
	{
		return new CompanionCommandEvent
		{
			Command = command.Kind switch
			{
				CompanionDeviceCommandKind.SetBrightness => "setBrightness",
				CompanionDeviceCommandKind.SetOrientation => "setOrientation",
				CompanionDeviceCommandKind.Vibrate => "vibrate",
				CompanionDeviceCommandKind.ScreenOn => "screenOn",
				CompanionDeviceCommandKind.ScreenOff => "screenOff",
				CompanionDeviceCommandKind.Focus => "focus",
				_ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown command.")
			},
			BrightnessPercent = command.BrightnessPercent,
			Orientation = command.Orientation
		};
	}

	private void Raise(EventHandler<CompanionDeviceEventArgs>? handlers, Guid deviceId, CompanionDevice? device)
	{
		if (handlers is null)
		{
			return;
		}

		var args = new CompanionDeviceEventArgs(deviceId, device);
		foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<CompanionDeviceEventArgs>>())
		{
			try
			{
				handler(this, args);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "A Companion device event handler failed for device {DeviceId}", deviceId);
			}
		}
	}

	private async Task CancelOnDeviceAsync(Guid deviceId, string requestId)
	{
		try
		{
			await SendWireAsync(deviceId,
				new CompanionCommandEvent { Command = CancelRequestCommand, RequestId = requestId },
				CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex,
				"Could not cancel Companion request {RequestId} on device {DeviceId}",
				requestId,
				deviceId);
		}
	}

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs change)
	{
		if (change.IsAvailable && string.Equals(change.IntegrationId, ResolveOwner(), StringComparison.Ordinal))
		{
			ScheduleCreationForConnectedDevices();
		}
	}

	internal void IntegrationStateChanged(string integrationId)
	{
		string? before;
		lock (_ownerGate)
		{
			before = _owner?.Id;
		}

		var owner = ResolveOwner();
		if (owner is not null
			&& (!string.Equals(owner, before, StringComparison.Ordinal)
				|| string.Equals(owner, integrationId, StringComparison.Ordinal) && OwnerIsPlugin()))
		{
			ScheduleCreationForConnectedDevices();
		}
	}

	private void OnBuiltInRegistrationCompleted()
	{
		if (ResolveOwner() is not null)
		{
			ScheduleCreationForConnectedDevices();
		}
	}

	private void OnPluginAccessChanged()
	{
		if (ResolveOwner() is not null && OwnerIsPlugin())
		{
			ScheduleCreationForConnectedDevices();
		}
	}

	// Kept for this run only. Plugins register concurrently with the built-ins, so a plugin is only
	// considered once every built-in has registered; a built-in owner is final.
	private string? ResolveOwner()
	{
		var integrations = _registry.Integrations;
		lock (_ownerGate)
		{
			if (_owner is { IsPlugin: false } fixedOwner)
			{
				return fixedOwner.Id;
			}

			if (integrations.FirstOrDefault(integration
					=> !IsPlugin(integration.Id) && DeclaresDeviceEntries(integration))
				is { } builtIn)
			{
				_owner = (builtIn.Id, false);
			}
			else if (_owner is null
				&& _builtInRegistration.IsComplete
				&& integrations.FirstOrDefault(integration
						=> IsPlugin(integration.Id) && DeclaresDeviceEntries(integration))
					is { } plugin)
			{
				_owner = (plugin.Id, true);
			}

			return _owner?.Id;
		}
	}

	private bool OwnerIsPlugin()
	{
		lock (_ownerGate)
		{
			return _owner is { IsPlugin: true };
		}
	}

	private static bool DeclaresDeviceEntries(IIntegration integration)
	{
		return integration is IDeclaredCapabilityKinds declared
			? declared.DeclaredCapabilityKinds.Contains(CapabilityKinds.CompanionDeviceEntries, StringComparer.Ordinal)
			: integration is ICompanionDeviceConfigEntries;
	}

	private bool IsPlugin(string integrationId)
	{
		return _registry.GetOrigin(integrationId) == IntegrationOrigin.Plugin;
	}

	private async Task<bool> OwnerMayUseDevicesAsync(string ownerId)
	{
		return _registry.Integrations.Any(integration
				=> string.Equals(integration.Id, ownerId, StringComparison.Ordinal))
			&& (!IsPlugin(ownerId)
				|| _pluginAccess is not null
				&& await _pluginAccess.EvaluateAsync(ownerId) == PluginCompanionDeviceAccess.Available);
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
					_unreadFormFactors.Remove(connectionId);
				}

				wasConnected = _states.TryRemove(deviceId, out _);
			}

			_creations.TryRemove(deviceId, out _);
			_requests.FailDevice(deviceId, CompanionDeviceFailure.Removed);
			if (wasConnected)
			{
				Raise(DeviceChanged, deviceId, null);
			}

			if (ResolveOwner() is { } ownerId && await FindEntryAsync(ownerId, deviceId) is not null)
			{
				await _coordinator().DeleteWithoutStoringDisabledAsync(ownerId, deviceId, cancellationToken);
			}
		}
		finally
		{
			gate.Release();
		}
	}

	internal Task WakeOnLanSent { get; private set; } = Task.CompletedTask;

	internal Task CreationFor(Guid deviceId)
	{
		return _creations.GetValueOrDefault(deviceId) ?? Task.CompletedTask;
	}

	private async Task EnsureConfigurationAsync(Guid deviceId)
	{
		var gate = GateFor(deviceId);
		await gate.WaitAsync();
		try
		{
			if (ResolveOwner() is not { } ownerId)
			{
				return;
			}

			await using var scope = _scopeFactory.CreateAsyncScope();
			var device = await scope.ServiceProvider.GetRequiredService<IDeviceRepository>().GetById(deviceId);
			if (device is null
				|| device.ClientType != DeviceClientType.Native
				|| _registry.IsExplicitlyDisabled(ownerId)
				|| !await OwnerMayUseDevicesAsync(ownerId))
			{
				return;
			}

			var coordinator = _coordinator();
			var existing = await scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>().Find(deviceId);
			if (existing is not null
				&& (await coordinator.DescribeAsync(ownerId, CancellationToken.None))
				.Any(entry => entry.Id == deviceId && entry.Usable))
			{
				return;
			}

			var disabledVersion = _registry.DisabledVersion(ownerId);
			if (_registry.IsExplicitlyDisabled(ownerId))
			{
				return;
			}

			var outcome = await coordinator.CompleteUnlessDisabledAsync(ownerId,
				deviceId,
				existing?.Title ?? device.Name,
				new Dictionary<string, JsonElement>(),
				CancellationToken.None);
			if (!outcome.Success)
			{
				if (!_registry.IsExplicitlyDisabled(ownerId))
				{
					_failedCreations[deviceId] = true;
					_logger.Warning("Could not create the Companion configuration for device {DeviceId}", deviceId);
				}

				return;
			}

			// CompleteAsync switches the integration on; a user who switched it off meanwhile keeps that choice.
			if (_registry.DisabledVersion(ownerId) != disabledVersion)
			{
				await scope.ServiceProvider
					.GetRequiredService<IUiTransportMessageHandler<SetIntegrationEnabledRequest,
						SetIntegrationEnabledResponse>>()
					.Handle(new SetIntegrationEnabledRequest { Id = ownerId, Enabled = false },
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

	private async Task<ConfigEntryRecord?> FindEntryAsync(string ownerId, Guid deviceId)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var entry = await scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>().Find(deviceId);
		return entry is not null && string.Equals(entry.IntegrationId, ownerId, StringComparison.Ordinal)
			? entry
			: null;
	}

	private SemaphoreSlim GateFor(Guid deviceId)
	{
		return _deviceGates.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
	}

	private async Task<(bool Read, string? FormFactor)> ReadFormFactorAsync(Guid deviceId)
	{
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var device = await scope.ServiceProvider.GetRequiredService<IDeviceRepository>().GetById(deviceId);
			return (true, device?.FormFactor switch
			{
				DeviceFormFactor.Phone => CompanionFormFactors.Phone,
				DeviceFormFactor.Tablet => CompanionFormFactors.Tablet,
				DeviceFormFactor.Desktop => CompanionFormFactors.Desktop,
				_ => null
			});
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not read the form factor of Companion device {DeviceId}", deviceId);
			return (false, null);
		}
	}

	private static CompanionDeviceState Sanitize(ReportCompanionStateRequest report, string? formFactor)
	{
		var capabilities = (report.Capabilities ?? [])
			.Where(_knownCapabilities.Contains)
			.ToHashSet(StringComparer.Ordinal);
		return new CompanionDeviceState
		{
			BatteryLevelPercent = Percent(report.BatteryLevelPercent),
			IsCharging = report.Charging,
			Orientation = report.Orientation is CompanionOrientations.Portrait or CompanionOrientations.Landscape
				? report.Orientation
				: null,
			ScreenBrightnessPercent = Percent(report.ScreenBrightnessPercent),
			Model = Text(report.Model),
			Platform = Text(report.Platform),
			AppVersion = Text(report.AppVersion),
			FormFactor = formFactor,
			Capabilities = capabilities,
			ConfirmsCommands = report.RequestableCapabilities is not null,
			RequestableCapabilities = (report.RequestableCapabilities ?? [])
				.Where(capability
					=> _requestableCapabilities.Contains(capability) && !capabilities.Contains(capability))
				.ToHashSet(StringComparer.Ordinal),
			InFocus = report.InFocus,
			NetworkType = report.NetworkType is CompanionNetworkTypes.Wifi
				or CompanionNetworkTypes.Cellular
				or CompanionNetworkTypes.Ethernet
				or CompanionNetworkTypes.Vpn
				or CompanionNetworkTypes.None
				? report.NetworkType
				: null,
			NetworkMetered = report.NetworkMetered,
			NetworkValidated = report.NetworkValidated,
			NetworkName = Text(report.NetworkName),
			CpuUsagePercent = Percent(report.CpuUsagePercent),
			MemoryUsedPercent = Percent(report.MemoryUsedPercent)
		};
	}

	private static int? Percent(int? value)
	{
		return value is { } percent ? Math.Clamp(percent, 0, 100) : null;
	}

	private static string? Text(string? value)
	{
		return value is { Length: > MaximumTextLength } ? value[..MaximumTextLength] : value;
	}
}
