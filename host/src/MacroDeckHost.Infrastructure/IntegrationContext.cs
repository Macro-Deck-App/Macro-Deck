using MacroDeck.Sdk;
using MacroDeck.Sdk.Colors;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;

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
		IUserNotifier notifications,
		IMessageChannel messages,
		IColorApi colors,
		INotificationService notificationService,
		IUiResourceRegistry? uiResources = null)
	{
		Colors = colors;
		Variables = variables;
		UserVariables = userVariables;
		Config = config;
		Deck = deck;
		Scripts = scripts;
		Widgets = widgets;
		Events = events;
		Notifications = notifications;
		Messages = messages;
		NotificationService = notificationService;
		UiResources = uiResources ?? NoUiResources.Instance;
	}

	public IVariableApi Variables { get; }

	public IUserVariableApi UserVariables { get; }

	public IIntegrationConfig Config { get; }

	public IDeckNavigator Deck { get; }

	public IScriptApi Scripts { get; }

	public IWidgetApi Widgets { get; }

	public IEventPublisher Events { get; }

	public IUserNotifier Notifications { get; }

	public IMessageChannel Messages { get; }

	public IColorApi Colors { get; }

	public INotificationService NotificationService { get; }

	public IUiResourceRegistry UiResources { get; }

	private sealed class NoUiResources : IUiResourceRegistry
	{
		public static readonly NoUiResources Instance = new();

		public Task<UiResource> RegisterAsync(string name,
			ReadOnlyMemory<byte> content,
			string mediaType,
			CancellationToken cancellationToken = default)
			=> Task.FromException<UiResource>(Unsupported());

		public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
			=> Task.FromException(Unsupported());

		private static UiResourceException Unsupported()
			=> new(UiResourceErrorCode.Unsupported, "This context cannot register UI resources.");
	}
}
