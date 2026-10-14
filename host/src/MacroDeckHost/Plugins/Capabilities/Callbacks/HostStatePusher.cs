using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.Notifications.Shell;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Widgets;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class HostStatePusher(
	IPluginSessionRegistry sessionRegistry,
	IDeckNavigator deckNavigator,
	IScriptApi scriptApi,
	IWidgetApi widgetApi,
	IEventBindingTracker bindingTracker,
	DeckClientTracker deckClients,
	IAdbManager adbManager,
	IPluginAdbAccessPolicy adbAccessPolicy,
	ILogger logger,
	IBatteryRegistry? batteries = null,
	HttpUserAgentState? userAgent = null,
	ICompanionDeviceApi? companionDevices = null,
	IPluginCompanionDeviceAccessPolicy? companionAccess = null,
	IMusicPlayerRegistry? musicPlayers = null,
	IMusicPlayerStateNotifier? musicPlayerNotifier = null,
	PluginDesktopNotificationCallbacks? desktopNotifications = null,
	IShellNotificationBridge? shellNotifications = null) :
	INotificationHandler<FolderCreatedNotification>,
	INotificationHandler<FolderUpdatedNotification>,
	INotificationHandler<FolderDeletedNotification>,
	INotificationHandler<FoldersReorderedNotification>,
	INotificationHandler<ProfileCreatedNotification>,
	INotificationHandler<ProfileUpdatedNotification>,
	INotificationHandler<ProfileDeletedNotification>,
	INotificationHandler<ScriptCreatedNotification>,
	INotificationHandler<ScriptUpdatedNotification>,
	INotificationHandler<ScriptDeletedNotification>,
	INotificationHandler<WidgetCreatedNotification>,
	INotificationHandler<WidgetUpdatedNotification>,
	INotificationHandler<WidgetDeletedNotification>,
	INotificationHandler<WidgetPositionsUpdatedNotification>,
	INotificationHandler<WidgetsCreatedNotification>,
	INotificationHandler<WidgetsUpdatedNotification>,
	INotificationHandler<WidgetsDeletedNotification>,
	INotificationHandler<AdbSettingsChangedNotification>,
	INotificationHandler<WidgetTypeCatalogChangedNotification>,
	IDisposable
{
	private readonly SemaphoreSlim _eventBindingsPush = new(1, 1);
	private int _subscribedToBindings;
	private readonly Lock _deckGate = new();
	private long _deckRevision;
	private readonly SemaphoreSlim _adbPush = new(1, 1);
	private readonly SemaphoreSlim _catalogWidgetsPush = new(1, 1);
	private long _adbRevision;
	private readonly SemaphoreSlim _batteriesPush = new(1, 1);
	private long _batteriesRevision;
	private readonly SemaphoreSlim _companionPush = new(1, 1);
	private long _companionRevision;

	private readonly Dictionary<string, PluginCompanionDeviceAccess> _companionAccessPushed
		= new(StringComparer.Ordinal);

	private readonly SemaphoreSlim _musicPlayersPush = new(1, 1);
	private readonly Lock _musicPlayersGate = new();
	private long _musicPlayersRevision;
	private IReadOnlyList<MusicPlayerInstanceStateDto>? _musicPlayersBroadcast;
	private readonly SemaphoreSlim _desktopNotificationsPush = new(1, 1);
	private readonly Dictionary<string, bool> _desktopNotificationsPushed = new(StringComparer.Ordinal);

	public void Dispose()
	{
		_eventBindingsPush.Dispose();
		_adbPush.Dispose();
		_catalogWidgetsPush.Dispose();
		_batteriesPush.Dispose();
		_companionPush.Dispose();
		_musicPlayersPush.Dispose();
		_desktopNotificationsPush.Dispose();
		if (Volatile.Read(ref _subscribedToBindings) == 1)
		{
			if (shellNotifications is not null)
			{
				shellNotifications.AttachmentChanged -= OnShellAttachmentChanged;
			}

			if (musicPlayerNotifier is not null)
			{
				musicPlayerNotifier.InstancesChanged -= OnMusicPlayersChanged;
			}

			if (companionDevices is not null)
			{
				companionDevices.DeviceChanged -= OnCompanionDeviceChanged;
			}

			if (companionAccess is not null)
			{
				companionAccess.Changed -= OnCompanionAccessChanged;
			}

			if (batteries is not null)
			{
				batteries.Changed -= OnBatteriesChanged;
			}

			if (userAgent is not null)
			{
				userAgent.Changed -= OnUserAgentChanged;
			}
		}
	}

	public Task PushAllAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		// Subscribed on the first registration: no plugin can receive a push before it has registered.
		if (Interlocked.Exchange(ref _subscribedToBindings, 1) == 0)
		{
			bindingTracker.Subscribe(PushEventBindingsIfConnectedAsync);
			deckClients.StateChanged += OnDeckClientsChanged;
			adbManager.SnapshotChanged += OnAdbSnapshotChanged;
			if (batteries is not null)
			{
				batteries.Changed += OnBatteriesChanged;
			}

			if (userAgent is not null)
			{
				userAgent.Changed += OnUserAgentChanged;
			}

			if (companionDevices is not null)
			{
				companionDevices.DeviceChanged += OnCompanionDeviceChanged;
			}

			if (companionAccess is not null)
			{
				companionAccess.Changed += OnCompanionAccessChanged;
			}

			if (musicPlayerNotifier is not null)
			{
				musicPlayerNotifier.InstancesChanged += OnMusicPlayersChanged;
			}

			if (shellNotifications is not null)
			{
				shellNotifications.AttachmentChanged += OnShellAttachmentChanged;
			}
		}

		return Task.WhenAll(PushDeckAsync(pluginId, cancellationToken),
			PushScriptsAsync(pluginId, cancellationToken),
			PushWidgetsAsync(pluginId, cancellationToken),
			PushEventBindingsAsync(pluginId, cancellationToken),
			PushAdbAsync(pluginId, cancellationToken),
			PushBatteriesAsync(pluginId, [], cancellationToken),
			PushHttpAsync(pluginId, cancellationToken),
			PushCompanionDevicesAsync(pluginId, true, cancellationToken),
			PushMusicPlayersAsync(pluginId, cancellationToken),
			PushDesktopNotificationsAsync(pluginId, true, cancellationToken));
	}

	public ValueTask Handle(AdbSettingsChangedNotification notification, CancellationToken cancellationToken)
	{
		adbAccessPolicy.Refresh(notification.Settings);
		return BroadcastAdbAsync(cancellationToken);
	}

	public ValueTask Handle(FolderCreatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(FolderUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(FolderDeletedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(FoldersReorderedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(ProfileCreatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(ProfileUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastDeckAsync(cancellationToken);
	}

	public ValueTask Handle(ScriptCreatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastScriptsAsync(cancellationToken);
	}

	public ValueTask Handle(ScriptUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastScriptsAsync(cancellationToken);
	}

	public ValueTask Handle(ScriptDeletedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastScriptsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetPositionsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		return BroadcastWidgetsAsync(cancellationToken);
	}

	// The registry publishes this while a plugin registers its types, so a slow or failing send to another
	// plugin must not hold up or fail that registration.
	public ValueTask Handle(WidgetTypeCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		_ = PushWidgetsAfterCatalogChangeAsync();
		return ValueTask.CompletedTask;
	}

	private async ValueTask BroadcastDeckAsync(CancellationToken cancellationToken)
	{
		var payload = BuildEnvelope(HostApis.Deck, BuildDeckState());
		await BroadcastAsync(payload, cancellationToken);
	}

	private async ValueTask BroadcastScriptsAsync(CancellationToken cancellationToken)
	{
		var scripts = scriptApi.GetScripts();

		var pluginIds = sessionRegistry.Snapshot()
			.Where(session => session.State == PluginSessionState.Connected)
			.Select(session => session.PluginId)
			.ToList();

		foreach (var pluginId in pluginIds)
		{
			await sessionRegistry.SendToPlugin(pluginId, ScriptsEnvelope(pluginId, scripts), cancellationToken);
		}
	}

	private ProtocolEnvelope ScriptsEnvelope(string pluginId, IReadOnlyList<Script> scripts)
	{
		return BuildEnvelope(HostApis.Scripts,
			ScriptWireCompatibility.ToWirePayload(scripts,
				sessionRegistry.HasHostApiFeature(pluginId, HostApiFeatures.ScriptInputColor)));
	}

	// Widgets differ by negotiated version (see WidgetStateWireCompatibility), so unlike
	// BroadcastDeckAsync this builds one envelope per plugin instead of sharing one.
	private async ValueTask BroadcastWidgetsAsync(CancellationToken cancellationToken)
	{
		var widgets = widgetApi.GetWidgets();

		var pluginIds = sessionRegistry.Snapshot()
			.Where(session => session.State == PluginSessionState.Connected)
			.Select(session => session.PluginId)
			.ToList();

		foreach (var pluginId in pluginIds)
		{
			var payload = BuildEnvelope(HostApis.Widgets,
				WidgetStateWireCompatibility.ToWirePayload(widgets, sessionRegistry.GetNegotiatedVersion(pluginId)));
			await sessionRegistry.SendToPlugin(pluginId, payload, cancellationToken);
		}
	}

	private Task<bool> PushDeckAsync(string pluginId, CancellationToken cancellationToken)
	{
		return sessionRegistry.SendToPlugin(pluginId,
			BuildEnvelope(HostApis.Deck, BuildDeckState()),
			cancellationToken);
	}

	private Task<bool> PushScriptsAsync(string pluginId, CancellationToken cancellationToken)
	{
		return sessionRegistry.SendToPlugin(pluginId,
			ScriptsEnvelope(pluginId, scriptApi.GetScripts()),
			cancellationToken);
	}

	private Task<bool> PushWidgetsAsync(string pluginId, CancellationToken cancellationToken)
	{
		return sessionRegistry.SendToPlugin(pluginId,
			BuildEnvelope(HostApis.Widgets,
				WidgetStateWireCompatibility.ToWirePayload(widgetApi.GetWidgets(),
					sessionRegistry.GetNegotiatedVersion(pluginId))),
			cancellationToken);
	}

	private Task PushEventBindingsIfConnectedAsync(string pluginId, IReadOnlyList<EventBindingDto> _)
	{
		var connected = sessionRegistry.Snapshot()
			.Any(session => session.State == PluginSessionState.Connected && session.PluginId == pluginId);

		return connected ? PushEventBindingsAsync(pluginId, CancellationToken.None) : Task.CompletedTask;
	}

	// Read and sent under one lock so a registration push and a change push cannot overtake each other
	// and leave the plugin holding the older list.
	private async Task PushEventBindingsAsync(string pluginId, CancellationToken cancellationToken)
	{
		await _eventBindingsPush.WaitAsync(cancellationToken);
		try
		{
			await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.EventBindings, bindingTracker.BindingsFor(pluginId)),
				cancellationToken);
		}
		finally
		{
			_eventBindingsPush.Release();
		}
	}

	// Revision and snapshot are taken together so a higher revision never carries older state; a
	// plugin drops a push whose revision it has already passed, so sends need no ordering of their own.
	private DeckStateDto BuildDeckState()
	{
		lock (_deckGate)
		{
			return new DeckStateDto
			{
				Folders = deckNavigator.GetFolders(),
				Profiles = deckNavigator.GetProfiles(),
				Clients = [.. deckNavigator.GetClients().Select(ToDto)],
				Revision = ++_deckRevision
			};
		}
	}

	private static DeckClientDto ToDto(DeckClient client)
	{
		return new DeckClientDto
		{
			ClientId = client.ClientId,
			DeviceId = client.DeviceId,
			ProfileId = client.ProfileId,
			FolderId = client.FolderId
		};
	}

	private void OnDeckClientsChanged()
	{
		_ = PushDeckClientsAsync();
	}

	private void OnAdbSnapshotChanged(object? sender, EventArgs e)
	{
		_ = PushAdbChangeAsync();
	}

	private async Task PushAdbChangeAsync()
	{
		try
		{
			await BroadcastAdbAsync(CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push adb state to plugins");
		}
	}

	private async ValueTask BroadcastAdbAsync(CancellationToken cancellationToken)
	{
		var pluginIds = sessionRegistry.Snapshot()
			.Where(session => session.State == PluginSessionState.Connected)
			.Select(session => session.PluginId)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		foreach (var pluginId in pluginIds)
		{
			await PushAdbAsync(pluginId, cancellationToken);
		}
	}

	// Serialized so a push evaluated against older access or devices can never carry the higher revision.
	private async Task<bool> PushAdbAsync(string pluginId, CancellationToken cancellationToken)
	{
		await _adbPush.WaitAsync(cancellationToken);
		try
		{
			var access = await adbAccessPolicy.EvaluateAsync(pluginId, cancellationToken);
			return await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.Adb, BuildAdbState(access)),
				cancellationToken);
		}
		finally
		{
			_adbPush.Release();
		}
	}

	private AdbStateDto BuildAdbState(PluginAdbAccess access)
	{
		var devices = adbManager.Devices;
		var attached = access == PluginAdbAccess.Available
			? devices.Where(device => device.State != AdbDeviceState.Disconnected).ToList()
			: [];
		return new AdbStateDto
		{
			Access = access switch
			{
				PluginAdbAccess.Available => AdbAccessStates.Available,
				PluginAdbAccess.NotAllowed => AdbAccessStates.NotAllowed,
				_ => AdbAccessStates.NotEnabled
			},
			Devices = [.. attached.Select(ToDto)],
			DefaultDeviceSerial = access == PluginAdbAccess.Available
				? DefaultDeviceSerial(adbManager.Status.DefaultDeviceSerial, devices, attached)
				: null,
			AdbUnavailable = access == PluginAdbAccess.Available && adbManager.Status.ResolvedExecutablePath is null
				? true
				: null,
			Revision = ++_adbRevision
		};
	}

	private static string? DefaultDeviceSerial(
		string? configured,
		IReadOnlyList<AdbDevice> devices,
		List<AdbDevice> attached)
	{
		if (!string.IsNullOrWhiteSpace(configured))
		{
			return devices.Any(device => string.Equals(device.Serial, configured, StringComparison.Ordinal))
				? configured
				: null;
		}

		return attached.Count == 1 ? attached[0].Serial : null;
	}

	private static AdbDeviceStateDto ToDto(AdbDevice device)
	{
		return new AdbDeviceStateDto
		{
			Serial = device.Serial,
			State = device.State switch
			{
				AdbDeviceState.Device => AdbDeviceStates.Online,
				AdbDeviceState.Authorizing => AdbDeviceStates.Connecting,
				AdbDeviceState.Unauthorized or AdbDeviceState.NoPermissions => AdbDeviceStates.Unauthorized,
				_ => AdbDeviceStates.Offline
			},
			Model = device.Model,
			Manufacturer = device.Manufacturer,
			Product = device.Product,
			StableId = device.StableId,
			LacksUsbPermission = device.State == AdbDeviceState.NoPermissions ? true : null,
			Mode = device.State switch
			{
				AdbDeviceState.Bootloader => AdbDeviceModes.Bootloader,
				AdbDeviceState.Recovery => AdbDeviceModes.Recovery,
				AdbDeviceState.Sideload => AdbDeviceModes.Sideload,
				AdbDeviceState.Unknown => AdbDeviceModes.Unknown,
				_ => null
			}
		};
	}

	private void OnBatteriesChanged(object? sender, BatteryDevicesChangedEventArgs e)
	{
		_ = PushBatteriesChangeAsync(e.RemovedIds);
	}

	// Every connected plugin gets every provider's devices, ADB phones included: reading battery devices
	// is deliberately unrestricted (see the batteries ADR), so there is no per-plugin filter here.
	private async Task PushBatteriesChangeAsync(IReadOnlyList<string> removedIds)
	{
		List<string> pluginIds;
		try
		{
			pluginIds = sessionRegistry.Snapshot()
				.Where(session => session.State == PluginSessionState.Connected)
				.Select(session => session.PluginId)
				.Distinct(StringComparer.Ordinal)
				.ToList();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push battery devices to plugins");
			return;
		}

		foreach (var pluginId in pluginIds)
		{
			try
			{
				await PushBatteriesAsync(pluginId, removedIds, CancellationToken.None);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				logger.ForContext<HostStatePusher>()
					.Error(exception, "Failed to push battery devices to plugin {PluginId}", pluginId);
			}
		}
	}

	// Serialized like the adb push, so a push built from an older device list never carries the higher revision.
	private async Task<bool> PushBatteriesAsync(
		string pluginId,
		IReadOnlyList<string> removedIds,
		CancellationToken cancellationToken)
	{
		if (batteries is null)
		{
			return false;
		}

		await _batteriesPush.WaitAsync(cancellationToken);
		try
		{
			var state = new BatteryStateDto
			{
				Devices = [.. batteries.Devices.Select(BatteryWire.ToDto)],
				RemovedIds = removedIds,
				Revision = ++_batteriesRevision
			};

			return await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.Batteries, state),
				cancellationToken);
		}
		finally
		{
			_batteriesPush.Release();
		}
	}

	private void OnCompanionDeviceChanged(object? sender, CompanionDeviceEventArgs e)
	{
		_ = BroadcastCompanionDevicesAsync(false);
	}

	private void OnCompanionAccessChanged()
	{
		_ = BroadcastCompanionDevicesAsync(true);
	}

	private async Task BroadcastCompanionDevicesAsync(bool accessChanged)
	{
		try
		{
			var pluginIds = sessionRegistry.Snapshot()
				.Where(session => session.State == PluginSessionState.Connected)
				.Select(session => session.PluginId)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			foreach (var pluginId in pluginIds)
			{
				await PushCompanionDevicesAsync(pluginId, accessChanged, CancellationToken.None);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push Companion devices to plugins");
		}
	}

	// Serialized like the adb push. A plugin that may not use Companion devices learns that once, not on
	// every state report of every device.
	private async Task<bool> PushCompanionDevicesAsync(
		string pluginId,
		bool always,
		CancellationToken cancellationToken)
	{
		if (companionDevices is null || companionAccess is null)
		{
			return false;
		}

		await _companionPush.WaitAsync(cancellationToken);
		try
		{
			var access = await companionAccess.EvaluateAsync(pluginId, cancellationToken);
			if (!always
				&& access == PluginCompanionDeviceAccess.NotAllowed
				&& _companionAccessPushed.TryGetValue(pluginId, out var pushed)
				&& pushed == PluginCompanionDeviceAccess.NotAllowed)
			{
				return false;
			}

			_companionAccessPushed[pluginId] = access;
			var state = new CompanionDevicesStateDto
			{
				Access = access == PluginCompanionDeviceAccess.Available
					? CompanionDeviceAccessStates.Available
					: CompanionDeviceAccessStates.NotAllowed,
				Devices = access == PluginCompanionDeviceAccess.Available
					? [.. companionDevices.Devices.Select(CompanionDeviceWire.ToDto)]
					: [],
				Revision = ++_companionRevision
			};

			return await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.CompanionDevices, state),
				cancellationToken);
		}
		finally
		{
			_companionPush.Release();
		}
	}

	private void OnMusicPlayersChanged(object? sender, EventArgs e)
	{
		_ = BroadcastMusicPlayersAsync();
	}

	// Every plugin reads every provider's players, as with artwork, so there is no per-plugin filter. The
	// notifier also fires when only widget options changed, which this push does not carry.
	private async Task BroadcastMusicPlayersAsync()
	{
		try
		{
			if (musicPlayers is null)
			{
				return;
			}

			var instances = MusicPlayerInstances();
			lock (_musicPlayersGate)
			{
				if (_musicPlayersBroadcast is { } previous && previous.SequenceEqual(instances))
				{
					return;
				}

				_musicPlayersBroadcast = instances;
			}

			var pluginIds = sessionRegistry.Snapshot()
				.Where(session => session.State == PluginSessionState.Connected)
				.Select(session => session.PluginId)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			foreach (var pluginId in pluginIds)
			{
				await PushMusicPlayersAsync(pluginId, CancellationToken.None);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push music players to plugins");
		}
	}

	// Serialized like the adb push, so a push built from an older list never carries the higher revision.
	private async Task<bool> PushMusicPlayersAsync(string pluginId, CancellationToken cancellationToken)
	{
		if (musicPlayers is null)
		{
			return false;
		}

		await _musicPlayersPush.WaitAsync(cancellationToken);
		try
		{
			IReadOnlyList<MusicPlayerInstanceStateDto> instances;
			try
			{
				instances = MusicPlayerInstances();
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				logger.ForContext<HostStatePusher>()
					.Error(exception, "Failed to list music players for plugin {PluginId}", pluginId);
				return false;
			}

			var state = new MusicPlayersStateDto { Instances = instances, Revision = ++_musicPlayersRevision };
			return await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.MusicPlayers, state),
				cancellationToken);
		}
		finally
		{
			_musicPlayersPush.Release();
		}
	}

	private IReadOnlyList<MusicPlayerInstanceStateDto> MusicPlayerInstances()
	{
		return musicPlayers is null
			? []
			: [.. MusicPlayerInstanceSnapshots.From(musicPlayers.GetInstances()).Select(MusicPlayerWire.ToDto)];
	}

	// Where the platform cannot show notifications, support depends on the desktop shell being attached.
	private void OnShellAttachmentChanged()
	{
		_ = BroadcastDesktopNotificationsAsync();
	}

	private async Task BroadcastDesktopNotificationsAsync()
	{
		try
		{
			var pluginIds = sessionRegistry.Snapshot()
				.Where(session => session.State == PluginSessionState.Connected)
				.Select(session => session.PluginId)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			foreach (var pluginId in pluginIds)
			{
				await PushDesktopNotificationsAsync(pluginId, false, CancellationToken.None);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>()
				.Error(exception, "Failed to push desktop notification support to plugins");
		}
	}

	// Serialized so a push read before a shell change never lands after the one read after it.
	// Task.Run: the first support check probes the platform, which can block for seconds on Linux.
	private async Task<bool> PushDesktopNotificationsAsync(
		string pluginId,
		bool always,
		CancellationToken cancellationToken)
	{
		if (desktopNotifications is null)
		{
			return false;
		}

		await _desktopNotificationsPush.WaitAsync(cancellationToken);
		try
		{
			var supported = await Task.Run(() => desktopNotifications.IsSupportedFor(pluginId), cancellationToken);
			if (!always && _desktopNotificationsPushed.TryGetValue(pluginId, out var pushed) && pushed == supported)
			{
				return false;
			}

			_desktopNotificationsPushed[pluginId] = supported;
			return await sessionRegistry.SendToPlugin(pluginId,
				BuildEnvelope(HostApis.DesktopNotifications,
					new DesktopNotificationsStateDto { Supported = supported }),
				cancellationToken);
		}
		finally
		{
			_desktopNotificationsPush.Release();
		}
	}

	private Task<bool> PushHttpAsync(string pluginId, CancellationToken cancellationToken)
	{
		return userAgent is null
			? Task.FromResult(false)
			: sessionRegistry.SendToPlugin(pluginId, HttpEnvelope(), cancellationToken);
	}

	private ProtocolEnvelope HttpEnvelope()
	{
		return BuildEnvelope(HostApis.Http, new HttpStateDto { UserAgent = userAgent?.Current ?? string.Empty });
	}

	private void OnUserAgentChanged()
	{
		_ = PushUserAgentChangeAsync();
	}

	private async Task PushUserAgentChangeAsync()
	{
		try
		{
			await BroadcastAsync(HttpEnvelope(), CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push the HTTP user agent to plugins");
		}
	}

	private async Task PushWidgetsAfterCatalogChangeAsync()
	{
		try
		{
			// One at a time, each reading the widgets once it holds the gate, so a plugin never receives an
			// older snapshot after a newer one while several types register at once.
			await _catalogWidgetsPush.WaitAsync();
			try
			{
				await BroadcastWidgetsAsync(CancellationToken.None);
			}
			finally
			{
				_catalogWidgetsPush.Release();
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>()
				.Error(exception, "Failed to push widgets to plugins after a widget type change");
		}
	}

	private async Task PushDeckClientsAsync()
	{
		try
		{
			await BroadcastDeckAsync(CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.ForContext<HostStatePusher>().Error(exception, "Failed to push client positions to plugins");
		}
	}

	private async Task BroadcastAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		var pluginIds = sessionRegistry.Snapshot()
			.Where(session => session.State == PluginSessionState.Connected)
			.Select(session => session.PluginId)
			.ToList();

		foreach (var pluginId in pluginIds)
		{
			await sessionRegistry.SendToPlugin(pluginId,
				envelope with { Id = Guid.CreateVersion7().ToString() },
				cancellationToken);
		}
	}

	private static ProtocolEnvelope BuildEnvelope(string api, object? data)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = api,
					Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};
	}
}
