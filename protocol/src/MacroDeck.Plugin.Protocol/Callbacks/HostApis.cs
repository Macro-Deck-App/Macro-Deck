namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The host APIs a plugin can reach through <c>host.invoke</c>, plus the push-only ones the host
/// serves over <c>host.state</c> alone (<see cref="EventBindings" />). <c>events</c> is deliberately
/// absent - <c>IEventPublisher.Publish</c> already has a dedicated message type, <c>event.publish</c>,
/// so routing it through the generic callback pair would just be a second way to say the same thing.
/// <c>ui</c> is present for the opposite reason - a UI provider's snapshots, patches and faults are
/// plugin-initiated and asynchronous, and no dedicated message type carries them.</summary>
public static class HostApis
{
	public const string Variables = "variables";

	public const string UserVariables = "user-variables";

	public const string Config = "config";

	public const string Deck = "deck";

	public const string Scripts = "scripts";

	public const string Widgets = "widgets";

	public const string Notifications = "notifications";

	public const string ActionInteractions = "action-interactions";

	public const string Ui = "ui";

	public const string Devices = "devices";

	/// <summary>A push-capable variable provider's values are plugin-initiated, asynchronous, and - unlike
	/// <c>state.update</c> - data-carrying, and no dedicated message type carries them, so this api
	/// qualifies for the same reason <see cref="Ui" /> does.</summary>
	public const string VariableValues = "variable-values";

	public const string Layouts = "layouts";

	public const string FolderViews = "folder-views";

	public const string WidgetTypes = "widget-types";

	/// <summary>Push-only: the triggers bound to the receiving plugin's own events, sent as <c>host.state</c>
	/// on registration and whenever that set changes. It has no <c>host.invoke</c> operations.</summary>
	public const string EventBindings = "event-bindings";

	/// <summary>
	/// The <c>screensavers</c> host api: a screensaver provider registers and withdraws the screensavers it
	/// offers.
	/// </summary>
	public const string ScreenSavers = "screensavers";

	/// <summary>
	/// The <c>adb</c> host api: operations on the Android devices the host's adb server sees, plus a
	/// <c>host.state</c> push of this plugin's access and the device list. Gated per plugin.
	/// </summary>
	public const string Adb = "adb";

	/// <summary>
	/// The <c>messaging</c> host api: publishes events, sends commands and requests to other plugins and
	/// integrations by topic, and replaces this plugin's subscriptions and handled topics. Deliveries
	/// arrive as <c>capability.invoke</c> of the <c>messaging</c> capability kind.
	/// </summary>
	public const string Messaging = "messaging";

	/// <summary>
	/// The <c>icon-packs</c> host api: the icon packs a plugin bundles in its artifact. Resolves one of their
	/// icons into a UI resource, and lets a self-registered development session sync the packs of its
	/// project. A host that predates it answers <c>CAPABILITY_UNSUPPORTED</c>.
	/// </summary>
	public const string IconPacks = "icon-packs";

	/// <summary>
	/// The <c>video-streams</c> host api: a video stream provider announces catalog changes, and reports
	/// session state and closes for the sessions the host opened on it.
	/// </summary>
	public const string VideoStreams = "video-streams";

	/// <summary>
	/// The <c>colors</c> host api: resolves colour values a plugin stored, including Color variable
	/// references, and replaces the table of colours the plugin watches. Watched values arrive as a
	/// revisioned <c>host.state</c> push of the whole resolved table, only while the plugin watches any. A
	/// host that predates it answers <c>CAPABILITY_UNSUPPORTED</c>.
	/// </summary>
	public const string Colors = "colors";

	/// <summary>
	/// The <c>batteries</c> host api: a battery provider publishes, removes and resynchronizes its devices,
	/// and every plugin receives the devices of all providers as a revisioned <c>host.state</c> push. A
	/// host that predates it answers <c>CAPABILITY_UNSUPPORTED</c> and pushes nothing.
	/// </summary>
	public const string Batteries = "batteries";

	/// <summary>
	/// The <c>weather</c> host api: a weather provider tells the host its station snapshots changed, so the
	/// host reads them again at once. Requires the negotiated <c>weather</c> capability. A host that predates
	/// it answers <c>CAPABILITY_UNSUPPORTED</c> and keeps reading the stations on its own interval.
	/// </summary>
	public const string Weather = "weather";

	/// <summary>
	/// The <c>locale</c> host api: the user's language and time format preferences, for text a plugin formats
	/// itself. A host that predates it answers <c>CAPABILITY_UNSUPPORTED</c>.
	/// </summary>
	public const string Locale = "locale";

	/// <summary>Push-only: what outgoing HTTP requests should carry, as <see cref="HttpStateDto" />, sent as
	/// <c>host.state</c> on registration and whenever it changes. It has no <c>host.invoke</c> operations. A
	/// host that predates it pushes nothing.</summary>
	public const string Http = "http";

	/// <summary>
	/// The <c>companion-devices</c> host api: commands and screenshots for the devices connected through the
	/// Companion app, plus a revisioned <c>host.state</c> push of this plugin's access and the devices. Gated per
	/// plugin by <c>host:companion-devices</c>. A host that predates it answers <c>CAPABILITY_UNSUPPORTED</c> and
	/// pushes nothing.
	/// </summary>
	public const string CompanionDevices = "companion-devices";

	/// <summary>
	/// The <c>music-players</c> host api: the album colour of any provider's player, plus a revisioned
	/// <c>host.state</c> push of the players of all providers, as <see cref="MusicPlayersStateDto" />. Not gated:
	/// every plugin may read every provider's players. A host that predates it answers
	/// <c>CAPABILITY_UNSUPPORTED</c> and pushes nothing.
	/// </summary>
	public const string MusicPlayers = "music-players";

	/// <summary>
	/// The <c>desktop-notifications</c> host api: shows a notification from the operating system of the computer
	/// Macro Deck runs on, plus a <c>host.state</c> push on registration of whether this plugin can, as
	/// <see cref="DesktopNotificationsStateDto" />. Gated per plugin by <c>host:desktop-notifications</c>. A host
	/// that predates it answers <c>CAPABILITY_UNSUPPORTED</c> and pushes nothing.
	/// </summary>
	public const string DesktopNotifications = "desktop-notifications";

	public static readonly IReadOnlyList<string> All =
	[
		Variables, UserVariables, Config, Deck, Scripts, Widgets, Notifications, ActionInteractions, Ui,
		Devices, VariableValues, Layouts, FolderViews, WidgetTypes, EventBindings, ScreenSavers, Adb, Messaging,
		IconPacks, VideoStreams, Colors, Batteries, Weather, Locale, Http, CompanionDevices, MusicPlayers,
		DesktopNotifications
	];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? api)
	{
		return api is not null && _known.Contains(api);
	}
}
