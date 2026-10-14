using MacroDeck.Sdk;
using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Colors;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Http;
using MacroDeck.Sdk.Locale;
using MacroDeck.Sdk.Messaging;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
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
		IUiResourceRegistry? uiResources = null,
		IBatteryApi? batteries = null,
		IAndroidDeviceManager? androidDevices = null,
		IWeatherProviderApi? weatherProvider = null,
		IIntegrationStorage? storage = null,
		ILocaleApi? locale = null,
		IHostHttp? http = null,
		ICompanionDeviceApi? companionDevices = null,
		IMusicPlayerApi? musicPlayers = null,
		IDesktopNotifier? desktopNotifications = null)
	{
		DesktopNotifications = desktopNotifications ?? UnsupportedDesktopNotifier.Instance;
		WeatherProvider = weatherProvider ?? UnsupportedWeatherProviderApi.Instance;
		MusicPlayers = musicPlayers ?? UnsupportedMusicPlayerApi.Instance;
		CompanionDevices = companionDevices ?? UnsupportedCompanionDeviceApi.Instance;
		Storage = storage ?? UnavailableIntegrationStorage.Instance;
		Locale = locale ?? LocalLocaleApi.Instance;
		Http = http ?? DefaultHostHttp.Instance;
		UiResources = uiResources ?? NoUiResources.Instance;
		Colors = colors;
		Batteries = batteries ?? UnsupportedBatteryApi.Instance;
		AndroidDevices = androidDevices ?? UnsupportedAndroidDeviceManager.Instance;
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

		public Task<UiResource> RegisterAsync(
			string name,
			ReadOnlyMemory<byte> content,
			string mediaType,
			CancellationToken cancellationToken = default)
		{
			return Task.FromException<UiResource>(Unsupported());
		}

		public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
		{
			return Task.FromException(Unsupported());
		}

		private static UiResourceException Unsupported()
		{
			return new UiResourceException(UiResourceErrorCode.Unsupported,
				"This context cannot register UI resources.");
		}
	}

	public IBatteryApi Batteries { get; }

	public IAndroidDeviceManager AndroidDevices { get; }

	public IWeatherProviderApi WeatherProvider { get; }

	public IIntegrationStorage Storage { get; }

	public ILocaleApi Locale { get; }

	public IHostHttp Http { get; }

	public ICompanionDeviceApi CompanionDevices { get; }

	public IMusicPlayerApi MusicPlayers { get; }

	public IDesktopNotifier DesktopNotifications { get; }
}
