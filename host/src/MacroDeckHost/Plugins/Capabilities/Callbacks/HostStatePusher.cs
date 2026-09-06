using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Widgets;
using Mediator;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class HostStatePusher(
	IPluginSessionRegistry sessionRegistry,
	IDeckNavigator deckNavigator,
	IScriptApi scriptApi,
	IWidgetApi widgetApi) :
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
	INotificationHandler<WidgetsDeletedNotification>
{
	public Task PushAllAsync(string pluginId, CancellationToken cancellationToken = default)
		=> Task.WhenAll(PushDeckAsync(pluginId, cancellationToken),
			PushScriptsAsync(pluginId, cancellationToken),
			PushWidgetsAsync(pluginId, cancellationToken));

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
		var payload = BuildEnvelope(HostApis.Deck,
			new DeckStateDto { Folders = deckNavigator.GetFolders(), Profiles = deckNavigator.GetProfiles() });
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
			BuildEnvelope(HostApis.Deck,
				new DeckStateDto { Folders = deckNavigator.GetFolders(), Profiles = deckNavigator.GetProfiles() }),
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
