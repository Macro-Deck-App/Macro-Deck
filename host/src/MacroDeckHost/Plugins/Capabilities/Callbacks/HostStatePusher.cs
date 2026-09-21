using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Triggers;
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
	ILogger logger) :
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
	IDisposable
{
	private readonly SemaphoreSlim _eventBindingsPush = new(1, 1);
	private int _subscribedToBindings;
	private readonly Lock _deckGate = new();
	private long _deckRevision;
	private readonly SemaphoreSlim _adbPush = new(1, 1);
	private long _adbRevision;

	public void Dispose()
	{
		_eventBindingsPush.Dispose();
		_adbPush.Dispose();
	}

	public Task PushAllAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		// Subscribed on the first registration: no plugin can receive a push before it has registered.
		if (Interlocked.Exchange(ref _subscribedToBindings, 1) == 0)
		{
			bindingTracker.Subscribe(PushEventBindingsIfConnectedAsync);
			deckClients.StateChanged += OnDeckClientsChanged;
			adbManager.SnapshotChanged += OnAdbSnapshotChanged;
		}

		return Task.WhenAll(PushDeckAsync(pluginId, cancellationToken),
			PushScriptsAsync(pluginId, cancellationToken),
			PushWidgetsAsync(pluginId, cancellationToken),
			PushEventBindingsAsync(pluginId, cancellationToken),
			PushAdbAsync(pluginId, cancellationToken));
	}

	public ValueTask Handle(AdbSettingsChangedNotification notification, CancellationToken cancellationToken)
	{
		adbAccessPolicy.Refresh(notification.Settings);
		return BroadcastAdbAsync(cancellationToken);
	}

	public ValueTask Handle(FolderCreatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(FolderUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(FolderDeletedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(FoldersReorderedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(ProfileCreatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(ProfileUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
		=> BroadcastDeckAsync(cancellationToken);

	public ValueTask Handle(ScriptCreatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastScriptsAsync(cancellationToken);

	public ValueTask Handle(ScriptUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastScriptsAsync(cancellationToken);

	public ValueTask Handle(ScriptDeletedNotification notification, CancellationToken cancellationToken)
		=> BroadcastScriptsAsync(cancellationToken);

	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetPositionsUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
		=> BroadcastWidgetsAsync(cancellationToken);

	private async ValueTask BroadcastDeckAsync(CancellationToken cancellationToken)
	{
		var payload = BuildEnvelope(HostApis.Deck, BuildDeckState());
		await BroadcastAsync(payload, cancellationToken);
	}

	private async ValueTask BroadcastScriptsAsync(CancellationToken cancellationToken)
	{
		var payload = BuildEnvelope(HostApis.Scripts, scriptApi.GetScripts());
		await BroadcastAsync(payload, cancellationToken);
	}

	// Widgets is the one push whose wire shape differs by negotiated version (see
	// WidgetStateWireCompatibility), so - unlike BroadcastDeckAsync/BroadcastScriptsAsync - this cannot
	// share one envelope across every connected plugin; it builds one per plugin instead.
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
		=> sessionRegistry.SendToPlugin(pluginId,
			BuildEnvelope(HostApis.Deck, BuildDeckState()),
			cancellationToken);

	private Task<bool> PushScriptsAsync(string pluginId, CancellationToken cancellationToken)
		=> sessionRegistry.SendToPlugin(pluginId,
			BuildEnvelope(HostApis.Scripts, scriptApi.GetScripts()),
			cancellationToken);

	private Task<bool> PushWidgetsAsync(string pluginId, CancellationToken cancellationToken)
		=> sessionRegistry.SendToPlugin(pluginId,
			BuildEnvelope(HostApis.Widgets,
				WidgetStateWireCompatibility.ToWirePayload(widgetApi.GetWidgets(),
					sessionRegistry.GetNegotiatedVersion(pluginId))),
			cancellationToken);

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
		=> new()
		{
			ClientId = client.ClientId,
			DeviceId = client.DeviceId,
			ProfileId = client.ProfileId,
			FolderId = client.FolderId
		};

	private void OnDeckClientsChanged() => _ = PushDeckClientsAsync();

	private void OnAdbSnapshotChanged(object? sender, EventArgs e) => _ = PushAdbChangeAsync();

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
		=> new()
		{
			Access = access switch
			{
				PluginAdbAccess.Available => AdbAccessStates.Available,
				PluginAdbAccess.NotAllowed => AdbAccessStates.NotAllowed,
				_ => AdbAccessStates.NotEnabled
			},
			Devices = access == PluginAdbAccess.Available
				? [.. adbManager.Devices.Where(device => device.State != AdbDeviceState.Disconnected).Select(ToDto)]
				: [],
			Revision = ++_adbRevision
		};

	private static AdbDeviceStateDto ToDto(AdbDevice device)
		=> new()
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
			Product = device.Product
		};

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
		=> new()
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
