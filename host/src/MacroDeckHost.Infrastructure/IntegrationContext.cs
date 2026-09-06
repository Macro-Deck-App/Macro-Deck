using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Infrastructure;

public class IntegrationContext : IIntegrationContext
{
	public IntegrationContext(
		IVariableApi variables,
		IUserVariableApi userVariables,
		IIntegrationConfig config,
		IDeckNavigator deck,
		IScriptApi scripts,
		IWidgetApi widgets,
		IEventPublisher events,
		IUserNotifier notifications)
	{
		Variables = variables;
		UserVariables = userVariables;
		Config = config;
		Deck = deck;
		Scripts = scripts;
		Widgets = widgets;
		Events = events;
		Notifications = notifications;
	}

	public IVariableApi Variables { get; }

	public IUserVariableApi UserVariables { get; }

	public IIntegrationConfig Config { get; }

	public IDeckNavigator Deck { get; }

	public IScriptApi Scripts { get; }

	public IWidgetApi Widgets { get; }

	public IEventPublisher Events { get; }

	public IUserNotifier Notifications { get; }
}
