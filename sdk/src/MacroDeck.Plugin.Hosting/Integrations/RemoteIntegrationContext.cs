using MacroDeck.Plugin.Hosting.Integrations.HostApis;
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
	RemoteUserNotifier notifications,
	RemoteMessageChannel messages,
	RemoteUiResourceRegistry uiResources,
	RemoteColorApi colors,
	RemoteNotificationService notificationService,
	RemoteBatteryApi? batteries = null,
	IAndroidDeviceManager? androidDevices = null,
	RemoteWeatherProviderApi? weatherProvider = null,
	IIntegrationStorage? storage = null,
	RemoteLocaleApi? locale = null,
	RemoteHostHttp? http = null,
	RemoteCompanionDeviceApi? companionDevices = null,
	RemoteMusicPlayerApi? musicPlayers = null,
	RemoteDesktopNotifier? desktopNotifications = null) : IIntegrationContext
{
	public IVariableApi Variables { get; } = variables;

	public IUserVariableApi UserVariables { get; } = userVariables;

	public IIntegrationConfig Config { get; } = config;

	public IDeckNavigator Deck { get; } = deck;

	public IScriptApi Scripts { get; } = scripts;

	public IWidgetApi Widgets { get; } = widgets;

	public IEventPublisher Events { get; } = events;

	public IUserNotifier Notifications { get; } = notifications;

	public IMessageChannel Messages { get; } = messages.Lifecycle;

	public IUiResourceRegistry UiResources { get; } = uiResources;

	public IColorApi Colors { get; } = colors.Lifecycle;

	public INotificationService NotificationService { get; } = notificationService;

	public IBatteryApi Batteries { get; } = batteries?.Lifecycle ?? UnsupportedBatteryApi.Instance;

	public IAndroidDeviceManager AndroidDevices { get; } = androidDevices ?? UnsupportedAndroidDeviceManager.Instance;

	public IWeatherProviderApi WeatherProvider { get; } =
		weatherProvider ?? (IWeatherProviderApi)UnsupportedWeatherProviderApi.Instance;

	public IIntegrationStorage Storage { get; } = storage ?? UnavailableIntegrationStorage.Instance;

	public ILocaleApi Locale { get; } = locale ?? (ILocaleApi)LocalLocaleApi.Instance;

	public IHostHttp Http { get; } = http ?? (IHostHttp)DefaultHostHttp.Instance;

	public ICompanionDeviceApi CompanionDevices { get; } =
		companionDevices ?? (ICompanionDeviceApi)UnsupportedCompanionDeviceApi.Instance;

	public IMusicPlayerApi MusicPlayers { get; } = musicPlayers?.Lifecycle ?? UnsupportedMusicPlayerApi.Instance;

	public IDesktopNotifier DesktopNotifications { get; } =
		desktopNotifications ?? (IDesktopNotifier)UnsupportedDesktopNotifier.Instance;
}
