using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Integrations;

/// <summary>
/// The context handed to an integration running out of process, once it is real: every member is a
/// proxy that reaches the host over the plugin protocol - <c>host.invoke</c>/<c>host.result</c> for a
/// request/response call, <c>event.publish</c> for events, and <c>host.state</c> for the synchronous
/// deck/script/widget pickers. See <c>Integrations.HostApis</c> for the individual adapters this
/// composes; this type is just their aggregate, mirroring <c>MacroDeckHost.Infrastructure.IntegrationContext</c>
/// on the host's own in-process side.
/// </summary>
internal sealed class RemoteIntegrationContext(
	RemoteVariableApi variables,
	RemoteUserVariableApi userVariables,
	RemoteIntegrationConfig config,
	RemoteDeckNavigator deck,
	RemoteScriptApi scripts,
	RemoteWidgetApi widgets,
	RemoteEventPublisher events,
	RemoteUserNotifier notifications) : IIntegrationContext
{
	public IVariableApi Variables { get; } = variables;

	public IUserVariableApi UserVariables { get; } = userVariables;

	public IIntegrationConfig Config { get; } = config;

	public IDeckNavigator Deck { get; } = deck;

	public IScriptApi Scripts { get; } = scripts;

	public IWidgetApi Widgets { get; } = widgets;

	public IEventPublisher Events { get; } = events;

	public IUserNotifier Notifications { get; } = notifications;
}
