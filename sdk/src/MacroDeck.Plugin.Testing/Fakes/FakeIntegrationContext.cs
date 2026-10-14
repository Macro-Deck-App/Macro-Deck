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

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// An <see cref="IIntegrationContext" /> built entirely from the fakes in this namespace, so a plugin
/// under test can be constructed and exercised with no host on the other end of any of its
/// capabilities.
///
/// <para>
/// Every capability is exposed twice: once through the <see cref="IIntegrationContext" /> interface, for
/// wiring into whatever the plugin under test expects, and once through its concrete fake type - e.g.
/// <see cref="Variables" /> is a <see cref="FakeVariableApi" /> - so a test can seed and inspect it
/// directly, with no downcast.
/// </para>
/// </summary>
public sealed class FakeIntegrationContext : IIntegrationContext
{
	private IAndroidDeviceManager? _registeredAndroidDevices;

	/// <summary>Creates a fresh, empty instance of every fake.</summary>
	public FakeIntegrationContext()
	{
		Variables = new FakeVariableApi();
		Config = new FakeIntegrationConfig();
		Deck = new FakeDeckNavigator();
		Scripts = new FakeScriptApi();
		Widgets = new FakeWidgetApi();
		// Deferring the widget check to the widget fake keeps one seeded widget enough for both.
		UserVariables = new FakeUserVariableApi(Widgets.Exists);
		Events = new FakeEventPublisher();
		Notifications = new FakeUserNotifier();
		NotificationService = new FakeNotificationService();
		Interactions = new FakeActionInteractions();
		Devices = new FakeDeviceProviderContext();
		VariableValues = new FakeVariableSink();
		Layouts = new FakeLayoutProviderContext();
		FolderViews = new FakeFolderViewProviderContext();
		WidgetTypes = new FakeWidgetTypeProviderContext();
		ScreenSavers = new FakeScreenSaverProviderContext();
		VideoStreams = new FakeVideoStreamProviderContext();
		Messages = new FakeMessageChannel();
		UiResources = new FakeUiResourceRegistry();
		Colors = new FakeColorApi();
		Batteries = new FakeBatteryApi();
		BatteryProvider = new FakeBatteryProviderContext();
		AndroidDevices = new FakeAndroidDeviceManager();
		WeatherProvider = new FakeWeatherProviderApi();
		Storage = new FakeIntegrationStorage();
		Locale = new FakeLocaleApi();
		Http = new FakeHostHttp();
		CompanionDevices = new FakeCompanionDeviceApi();
		MusicPlayers = new FakeMusicPlayerApi();
		DesktopNotifications = new FakeDesktopNotifier();
	}

	/// <summary>The plugin's own variables - see <see cref="FakeVariableApi" />.</summary>
	public FakeVariableApi Variables { get; }

	/// <summary>The user's variables - see <see cref="FakeUserVariableApi" />.</summary>
	public FakeUserVariableApi UserVariables { get; }

	/// <summary>This integration's config entries and secrets - see <see cref="FakeIntegrationConfig" />.</summary>
	public FakeIntegrationConfig Config { get; }

	/// <summary>Deck navigation - see <see cref="FakeDeckNavigator" />.</summary>
	public FakeDeckNavigator Deck { get; }

	/// <summary>The user's stored scripts - see <see cref="FakeScriptApi" />.</summary>
	public FakeScriptApi Scripts { get; }

	/// <summary>Widget appearance changes - see <see cref="FakeWidgetApi" />.</summary>
	public FakeWidgetApi Widgets { get; }

	/// <summary>This integration's raised events - see <see cref="FakeEventPublisher" />.</summary>
	public FakeEventPublisher Events { get; }

	/// <summary>
	/// Notifications raised into the host's notification center - see <see cref="FakeUserNotifier" />.
	/// </summary>
	public FakeUserNotifier Notifications { get; }

	/// <summary>Toasts and dialogs the plugin shows - see <see cref="FakeNotificationService" />.</summary>
	public FakeNotificationService NotificationService { get; }

	/// <summary>
	/// The action-interactions fake for this context. Not part of <see cref="IIntegrationContext" />
	/// - <c>IActionInteractions</c> reaches an action execution over the wire instead, answered by
	/// <c>Internal.HostInvokeDispatcher</c>'s handling of the action-interactions <c>host.invoke</c> API
	/// - this is just a convenient single source for both, so a test host can be wired up from one context.
	/// </summary>
	public FakeActionInteractions Interactions { get; }

	/// <summary>
	/// The device registry fake for this context. Not part of <see cref="IIntegrationContext" /> either -
	/// a device provider is handed its own <c>IDeviceProviderContext</c>, and an out-of-process provider
	/// reaches the host over the wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the
	/// devices <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeDeviceProviderContext Devices { get; }

	/// <summary>
	/// The variable push sink fake for this context. Not part of <see cref="IIntegrationContext" />
	/// either - a push-capable variable catalog is handed its own <c>IVariableSink</c> through
	/// <c>IVariableProvider.OnAttachedAsync</c>, and an out-of-process provider reaches the host over the
	/// wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the <c>variable-values</c>
	/// <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeVariableSink VariableValues { get; }

	/// <summary>
	/// The layout registry fake for this context. Not part of <see cref="IIntegrationContext" /> either -
	/// a layout provider is handed its own <c>ILayoutProviderContext</c>, and an out-of-process provider
	/// reaches the host over the wire, answered by <c>Internal.HostInvokeDispatcher</c>'s handling of the
	/// layouts <c>host.invoke</c> API - see <see cref="Interactions" />'s identical remarks.
	/// </summary>
	public FakeLayoutProviderContext Layouts { get; }

	/// <summary>
	/// The folder view catalog fake for this context. Not part of <see cref="IIntegrationContext" /> for
	/// the same reason <see cref="Layouts" /> is not - a folder view provider is handed its own
	/// <c>IFolderViewProviderContext</c>, and an out-of-process provider reaches the host over the wire.
	/// </summary>
	public FakeFolderViewProviderContext FolderViews { get; }

	/// <summary>
	/// The widget type catalog fake for this context. Not part of <see cref="IIntegrationContext" /> for
	/// the same reason <see cref="FolderViews" /> is not - a widget type provider is handed its own
	/// <c>IWidgetTypeProviderContext</c>, and an out-of-process provider reaches the host over the wire.
	/// </summary>
	public FakeWidgetTypeProviderContext WidgetTypes { get; }

	/// <summary>
	/// The screensaver catalog fake for this context. Not part of <see cref="IIntegrationContext" /> for the
	/// same reason <see cref="FolderViews" /> is not.
	/// </summary>
	public FakeScreenSaverProviderContext ScreenSavers { get; }

	/// <summary>
	/// The video stream provider fake for this context. Not part of <see cref="IIntegrationContext" /> for
	/// the same reason <see cref="FolderViews" /> is not.
	/// </summary>
	public FakeVideoStreamProviderContext VideoStreams { get; }

	/// <summary>The message channel - see <see cref="FakeMessageChannel" />.</summary>
	public FakeMessageChannel Messages { get; }

	/// <summary>Registered UI resources - see <see cref="FakeUiResourceRegistry" />.</summary>
	public FakeUiResourceRegistry UiResources { get; }

	/// <inheritdoc />
	IMessageChannel IIntegrationContext.Messages => Messages;

	/// <summary>Colour resolution and watches - see <see cref="FakeColorApi" />.</summary>
	public FakeColorApi Colors { get; }

	/// <inheritdoc />
	IColorApi IIntegrationContext.Colors => Colors;

	/// <summary>The battery devices of every provider, as a reader sees them - see <see cref="FakeBatteryApi" />.</summary>
	public FakeBatteryApi Batteries { get; }

	/// <inheritdoc />
	IBatteryApi IIntegrationContext.Batteries => Batteries;

	/// <summary>
	/// The battery catalog fake a battery provider publishes into. Not part of <see cref="IIntegrationContext" />
	/// for the same reason <see cref="Devices" /> is not: a battery provider is handed its own
	/// <c>IBatteryProviderContext</c>, and an out-of-process provider reaches the host over the wire, answered
	/// by <c>Internal.HostInvokeDispatcher</c>'s handling of the batteries <c>host.invoke</c> API.
	/// </summary>
	public FakeBatteryProviderContext BatteryProvider { get; }

	/// <summary>
	/// The Android devices - see <see cref="FakeAndroidDeviceManager" />. <see cref="PluginTestHarness" /> also
	/// registers it as the plugin's <see cref="IAndroidDeviceManager" />, with access
	/// <see cref="AndroidDeviceAccess.Unsupported" /> until the test calls
	/// <see cref="FakeAndroidDeviceManager.SetAccess" />. A plugin that registers its own manager keeps it, and
	/// <see cref="IIntegrationContext.AndroidDevices" /> then returns that one, as on a real host.
	/// </summary>
	public FakeAndroidDeviceManager AndroidDevices { get; }

	/// <inheritdoc />
	IAndroidDeviceManager IIntegrationContext.AndroidDevices => _registeredAndroidDevices ?? AndroidDevices;

	/// <summary>What a weather provider reported about its stations - see <see cref="FakeWeatherProviderApi" />.</summary>
	public FakeWeatherProviderApi WeatherProvider { get; }

	/// <inheritdoc />
	IWeatherProviderApi IIntegrationContext.WeatherProvider => WeatherProvider;

	internal void UseRegisteredAndroidDevices(IAndroidDeviceManager androidDevices)
	{
		_registeredAndroidDevices = ReferenceEquals(androidDevices, AndroidDevices) ? null : androidDevices;
	}

	/// <summary>This integration's stored documents - see <see cref="FakeIntegrationStorage" />.</summary>
	public FakeIntegrationStorage Storage { get; }

	/// <inheritdoc />
	IIntegrationStorage IIntegrationContext.Storage => Storage;

	/// <summary>The user's language and time format - see <see cref="FakeLocaleApi" />.</summary>
	public FakeLocaleApi Locale { get; }

	/// <inheritdoc />
	ILocaleApi IIntegrationContext.Locale => Locale;

	/// <summary>What outgoing requests carry - see <see cref="FakeHostHttp" />.</summary>
	public FakeHostHttp Http { get; }

	/// <inheritdoc />
	IHostHttp IIntegrationContext.Http => Http;

	/// <summary>
	/// The Companion devices - see <see cref="FakeCompanionDeviceApi" />. <see cref="PluginTestHarness" /> also
	/// registers it as the plugin's <see cref="ICompanionDeviceApi" />, with access
	/// <see cref="CompanionDeviceAccess.Unsupported" /> until the test calls
	/// <see cref="FakeCompanionDeviceApi.SetAccess" />.
	/// </summary>
	public FakeCompanionDeviceApi CompanionDevices { get; }

	/// <inheritdoc />
	ICompanionDeviceApi IIntegrationContext.CompanionDevices => CompanionDevices;

	/// <summary>The music players of every provider and their album colours - see <see cref="FakeMusicPlayerApi" />.</summary>
	public FakeMusicPlayerApi MusicPlayers { get; }

	/// <inheritdoc />
	IMusicPlayerApi IIntegrationContext.MusicPlayers => MusicPlayers;

	/// <summary>The operating system notifications shown - see <see cref="FakeDesktopNotifier" />.
	/// <see cref="PluginTestHarness" /> also registers it as the plugin's <see cref="IDesktopNotifier" />.</summary>
	public FakeDesktopNotifier DesktopNotifications { get; }

	/// <inheritdoc />
	IDesktopNotifier IIntegrationContext.DesktopNotifications => DesktopNotifications;

	/// <inheritdoc />
	IUiResourceRegistry IIntegrationContext.UiResources => UiResources;

	/// <inheritdoc />
	IVariableApi IIntegrationContext.Variables => Variables;

	/// <inheritdoc />
	IUserVariableApi IIntegrationContext.UserVariables => UserVariables;

	/// <inheritdoc />
	IIntegrationConfig IIntegrationContext.Config => Config;

	/// <inheritdoc />
	IDeckNavigator IIntegrationContext.Deck => Deck;

	/// <inheritdoc />
	IScriptApi IIntegrationContext.Scripts => Scripts;

	/// <inheritdoc />
	IWidgetApi IIntegrationContext.Widgets => Widgets;

	/// <inheritdoc />
	IEventPublisher IIntegrationContext.Events => Events;

	/// <inheritdoc />
	IUserNotifier IIntegrationContext.Notifications => Notifications;

	/// <inheritdoc />
	INotificationService IIntegrationContext.NotificationService => NotificationService;
}
