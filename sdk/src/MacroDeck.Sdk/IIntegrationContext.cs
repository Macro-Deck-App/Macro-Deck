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

namespace MacroDeck.Sdk;

public interface IIntegrationContext
{
	/// <summary>Variables owned by this integration.</summary>
	IVariableApi Variables { get; }

	/// <summary>User-owned variables writable by name.</summary>
	IUserVariableApi UserVariables { get; }

	/// <summary>Configuration entries created by this integration's config flow.</summary>
	IIntegrationConfig Config { get; }

	/// <summary>Deck and folder navigation.</summary>
	IDeckNavigator Deck { get; }

	/// <summary>Access to stored scripts.</summary>
	IScriptApi Scripts { get; }

	/// <summary>Runtime and persisted widget appearance updates.</summary>
	IWidgetApi Widgets { get; }

	/// <summary>Publishes events declared by this integration. Safe to retain for the process lifetime.</summary>
	IEventPublisher Events { get; }

	/// <summary>Publishes notifications to the host notification center.</summary>
	IUserNotifier Notifications { get; }

	/// <summary>
	/// Talks to other plugins and integrations by topic. Registrations made here are released when the
	/// integration is shut down or initialized again. A context from a Macro Deck without a message
	/// channel, and any implementation that does not override this member, throws
	/// <see cref="MessageChannelException" /> with <see cref="MessageChannelErrorCode.Unsupported" />.
	/// </summary>
	IMessageChannel Messages => UnsupportedMessageChannel.Instance;

	/// <summary>
	/// Registers images this integration's own trees show. A context from a Macro Deck that cannot register
	/// UI resources, and any implementation that does not override this member, throws
	/// <see cref="UiResourceException" /> with <see cref="UiResourceErrorCode.Unsupported" />.
	/// </summary>
	IUiResourceRegistry UiResources => UnsupportedUiResourceRegistry.Instance;

	/// <summary>
	/// Resolves and watches colour values, including Color variable references, from this integration's
	/// own stored settings. Watches made here are released when the integration is shut down or initialized
	/// again. A context from a Macro Deck that cannot resolve colours, and any implementation that does not
	/// override this member, resolves fixed colours locally, resolves every reference to <c>null</c>, and
	/// delivers a watch's value once.
	/// </summary>
	IColorApi Colors => LocalColorApi.Instance;

	/// <summary>
	/// Shows toasts and dialogs on connected clients and keeps Inbox entries, and returns what the user
	/// answered. A context from a Macro Deck without this service, and any implementation that does not
	/// override this member, throws <see cref="NotificationException" /> with
	/// <see cref="NotificationErrorCode.Unsupported" />.
	/// </summary>
	INotificationService NotificationService => UnsupportedNotificationService.Instance;

	/// <summary>
	/// Reads the battery devices every provider published, including other plugins'. Handlers of
	/// <see cref="IBatteryApi.Changed" /> subscribed here are released when the integration is shut down or
	/// initialized again. A context from a Macro Deck without battery devices, and any implementation that
	/// does not override this member, reports no devices and never raises the event.
	/// </summary>
	IBatteryApi Batteries => UnsupportedBatteryApi.Instance;

	/// <summary>
	/// The Android devices Macro Deck's own adb sees. In a plugin it is the same manager dependency injection
	/// resolves, with the same access rules; an integration built into Macro Deck needs only ADB to be
	/// enabled. A context from a Macro Deck that does not offer ADB to integrations, and any implementation
	/// that does not override this member, reports
	/// <see cref="AndroidDeviceAccess.Unsupported" />, no devices, and never raises an event.
	/// </summary>
	IAndroidDeviceManager AndroidDevices => UnsupportedAndroidDeviceManager.Instance;

	/// <summary>
	/// Tells Macro Deck when this integration's weather stations have new data, so Weather widgets follow
	/// at once. A context from a Macro Deck without it, and any implementation that does not override this
	/// member, ignores the notice; the stations are then still read about once a minute.
	/// </summary>
	IWeatherProviderApi WeatherProvider => UnsupportedWeatherProviderApi.Instance;

	/// <summary>
	/// Text documents this integration keeps for itself across restarts, see <see cref="IIntegrationStorage" />.
	/// A plugin's storage lives in the data directory Macro Deck gives it. A context from a Macro Deck without
	/// integration storage, a plugin started without a data directory, and any implementation that does not
	/// override this member, read every key as missing and throw <see cref="InvalidOperationException" /> on a
	/// write.
	/// </summary>
	IIntegrationStorage Storage => UnavailableIntegrationStorage.Instance;

	/// <summary>
	/// The user's language and time format preferences. A context from a Macro Deck that cannot report them,
	/// and any implementation that does not override this member, answer the process's current culture with a
	/// 24-hour clock.
	/// </summary>
	ILocaleApi Locale => LocalLocaleApi.Instance;

	/// <summary>
	/// What outgoing HTTP and WebSocket requests should carry, such as the User-Agent the user configured. A
	/// context from a Macro Deck that does not send it, and any implementation that does not override this
	/// member, answer <see cref="DefaultHostHttp" />.
	/// </summary>
	IHostHttp Http => DefaultHostHttp.Instance;

	/// <summary>
	/// The phones and tablets connected through the Companion app, see <see cref="ICompanionDeviceApi" />. In a plugin
	/// it is the same instance dependency injection resolves, with the same access rules; an integration built into
	/// Macro Deck always has access. A context from a Macro Deck without Companion devices, and any implementation
	/// that does not override this member, reports <see cref="CompanionDeviceAccess.Unsupported" />, no devices, and
	/// never raises an event.
	/// </summary>
	ICompanionDeviceApi CompanionDevices => UnsupportedCompanionDeviceApi.Instance;

	/// <summary>
	/// Reads the music players of every provider, including other plugins', and their album colours, see
	/// <see cref="IMusicPlayerApi" />. A context from a Macro Deck without it, and any implementation that does
	/// not override this member, reports no players, never raises the event and knows no album colour.
	/// </summary>
	IMusicPlayerApi MusicPlayers => UnsupportedMusicPlayerApi.Instance;

	/// <summary>
	/// Shows notifications from the operating system of the computer Macro Deck runs on, see
	/// <see cref="IDesktopNotifier" /> for when to use it rather than <see cref="NotificationService" /> or
	/// <see cref="Notifications" />. In a plugin it is the same instance dependency injection resolves. A context
	/// from a Macro Deck without desktop notifications, and any implementation that does not override this member,
	/// reports <see cref="IDesktopNotifier.IsSupported" /> as <c>false</c> and shows nothing.
	/// </summary>
	IDesktopNotifier DesktopNotifications => UnsupportedDesktopNotifier.Instance;
}
