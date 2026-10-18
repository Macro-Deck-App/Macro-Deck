namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The operation vocabulary for each <see cref="HostApis" /> member, in the same nested-class
/// idiom as <c>CapabilityOperations</c>.</summary>
public static class HostOperations
{
	public static class Variables
	{
		public const string List = "list";

		public const string Get = "get";

		public const string Create = "create";

		public const string Set = "set";

		public const string Delete = "delete";

		/// <summary>Asks the host to read variables of the calling plugin again on its next poll instead of at
		/// each variable's refresh interval: every declared one when it carries no arguments, or only those named
		/// by <see cref="VariablesRefreshArguments.DefinitionIds" />, declared variables and polled catalog
		/// resources a user bound alike. Requests that arrive while one is pending are merged. A host that
		/// predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>; the SDK treats that as a no-op. A host
		/// that predates the definition ids ignores them and reads every declared variable, and an older host
		/// ignores catalog resource ids.</summary>
		public const string Refresh = "refresh";

		/// <summary>Opens a history window on one of the calling plugin's own global variables and answers
		/// its window id. A host that predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>; the SDK
		/// reports that as an unsupported variable history.</summary>
		public const string HistoryOpen = "history.open";

		/// <summary>Answers the samples a window opened with <see cref="HistoryOpen" /> holds, oldest
		/// first.</summary>
		public const string HistoryRead = "history.read";

		/// <summary>Closes a window opened with <see cref="HistoryOpen" />. Closing an unknown window is not
		/// an error.</summary>
		public const string HistoryClose = "history.close";

		/// <summary>Records bindings from the calling plugin's own variable catalog resources to variables,
		/// see <see cref="VariablesBindArguments" />, and answers a <see cref="VariablesBindResult" />. A host
		/// that predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>; the SDK reports that as not
		/// supported.</summary>
		public const string Bind = "bind";

		public static readonly IReadOnlyList<string> All =
			[List, Get, Create, Set, Delete, Refresh, HistoryOpen, HistoryRead, HistoryClose, Bind];
	}

	public static class UserVariables
	{
		public const string Apply = "apply";

		public const string Create = "create";

		public static readonly IReadOnlyList<string> All = [Apply, Create];
	}

	public static class Config
	{
		public const string Entries = "entries";

		public const string GetString = "get-string";

		public const string GetSecret = "get-secret";

		public const string SetString = "set-string";

		public const string SetSecret = "set-secret";

		public static readonly IReadOnlyList<string> All = [Entries, GetString, GetSecret, SetString, SetSecret];
	}

	public static class Deck
	{
		public const string ChangeFolder = "change-folder";

		public const string ChangeProfile = "change-profile";

		public const string Parent = "parent";

		public const string Back = "back";

		public static readonly IReadOnlyList<string> All = [ChangeFolder, ChangeProfile, Parent, Back];
	}

	public static class Scripts
	{
		public const string Run = "run";

		public static readonly IReadOnlyList<string> All = [Run];
	}

	public static class Widgets
	{
		public const string Apply = "apply";

		/// <summary>Tells the host an icon-provider action's identity changed, so widgets following it
		/// re-read it now rather than at the next poll. See
		/// <c>MacroDeck.Sdk.Widgets.IWidgetApi.InvalidateIconAsync</c>. Additive - a v1 plugin can call it
		/// exactly as it can be an icon provider.</summary>
		public const string InvalidateIcon = "invalidate-icon";

		/// <summary>Answers the stored data of a widget whose type the calling plugin registered, or no
		/// result for any other widget. A host that predates the operation answers
		/// <c>CAPABILITY_UNSUPPORTED</c>; the SDK reports that as no data.</summary>
		public const string GetData = "get-data";

		public static readonly IReadOnlyList<string> All = [Apply, InvalidateIcon, GetData];
	}

	public static class Notifications
	{
		public const string Notify = "notify";

		public const string Dismiss = "dismiss";

		/// <summary>
		/// Shows a notification on the targeted clients and returns as soon as it is open, never when a
		/// user answers: <c>host.invoke</c> carries a fixed request deadline. How the notification ends
		/// arrives separately, as a <c>ui</c>/<c>notification.result</c> capability invoke. Additive - a
		/// host that predates it answers an unsupported capability.
		/// </summary>
		public const string Show = "show";

		public const string Update = "update";

		public const string Close = "close";

		public static readonly IReadOnlyList<string> All = [Notify, Dismiss, Show, Update, Close];
	}

	public static class ActionInteractions
	{
		public const string RequestItemPicker = "request-item-picker";

		public const string RequestDevicePicker = "request-device-picker";

		/// <summary>
		/// Opens a Macro Deck UI modal on the client that triggered an action. Returns as soon as the modal
		/// is open, never when the user answers: <c>host.invoke</c> carries a fixed request deadline, and a
		/// person is not bound by it. The answer arrives separately, as a <c>ui</c>/<c>modal.result</c>
		/// capability invoke.
		/// </summary>
		public const string ShowModal = "show-modal";

		public static readonly IReadOnlyList<string> All = [RequestItemPicker, RequestDevicePicker, ShowModal];
	}

	public static class Ui
	{
		public const string Snapshot = "snapshot";

		public const string Patch = "patch";

		public const string Fault = "fault";

		/// <summary>Ends a session so that every attached client opens it again with the provider's current
		/// code, without reporting a fault. Sent by the SDK when .NET Hot Reload updates the plugin. A host
		/// that predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string Reload = "reload";

		/// <summary>Registers bytes the plugin uploaded as kind <c>ui-resource</c> under a plugin-chosen
		/// name and answers the resource handle to reference from a tree. Registering a name again replaces
		/// the resource. Answers <c>uploadRequired</c> when the host does not hold the bytes yet.</summary>
		public const string RegisterResource = "register-resource";

		/// <summary>Removes a resource this plugin registered. An unknown name is not an error.</summary>
		public const string RemoveResource = "remove-resource";

		/// <summary>Registers the artwork of any music player, named by its qualified instance id and artwork id,
		/// as a resource of this plugin under a plugin-chosen name, and answers the resource handle. Counts
		/// against the plugin's UI resource quota like <c>register-resource</c>. An unknown instance or artwork
		/// answers no <c>resource</c>. A host that predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string RegisterMusicPlayerArtwork = "register-music-player-artwork";

		/// <summary>Shows a failure on the client that raised an event in one of the plugin's sessions, the
		/// way a failed action is shown. Delivered only while that client is attached to the session. A
		/// host that predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string OriginReportFailure = "origin.report-failure";

		/// <summary>Opens one of the plugin's dialogs on the client that raised an event in one of its
		/// sessions, while that client is attached to it. Answers like
		/// <c>action-interactions/show-modal</c>. A host that predates the operation answers
		/// <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string OriginShowModal = "origin.show-modal";

		/// <summary>Opens an absolute <c>http</c> or <c>https</c> URL in the browser of the client that raised an
		/// event in one of the plugin's sessions. Delivered only to the connection that raised the event, only
		/// while it is attached and declared it can open links, and only shortly after that event. A host that
		/// predates the operation answers <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string OriginOpenUrl = "origin.open-url";

		/// <summary>Reports values for the declared parameters of the action an action configuration session
		/// configures. Each value is treated as a user edit of that parameter; undeclared, <c>Secret</c> and
		/// <c>Password</c> parameters and values of the wrong shape are dropped, and a session that configures
		/// no action ignores the report. A host that predates the operation answers
		/// <c>CAPABILITY_UNSUPPORTED</c>.</summary>
		public const string ReportParameterValues = "report-parameter-values";

		public static readonly IReadOnlyList<string> All =
		[
			Snapshot, Patch, Fault, Reload, RegisterResource, RemoveResource, RegisterMusicPlayerArtwork,
			OriginReportFailure, OriginShowModal, OriginOpenUrl, ReportParameterValues
		];
	}

	public static class IconPacks
	{
		/// <summary>Replaces the calling development session's bundled icon packs with the complete declared
		/// set: adds, replaces and removes packs by key. Answers <c>uploadRequired</c> with the content hashes
		/// the host does not hold yet; the plugin uploads them as kind <c>icon-pack</c> and syncs again.
		/// Refused with <c>ICON_PACK_SYNC_NOT_ALLOWED</c> unless the session is self-registered.</summary>
		public const string SyncBundled = "sync-bundled";

		/// <summary>Answers a UI resource handle for one icon of the calling plugin's bundled packs, by pack key
		/// and icon name. The handle is served by the host's icon store, uses no UI resource quota and stays
		/// valid across host restarts. Unknown key or name: <c>PLUGIN_ICON_NOT_FOUND</c>.</summary>
		public const string GetIconResource = "get-icon-resource";

		/// <summary>Answers a UI resource handle for one icon of any installed icon pack, by the icon's id. The
		/// handle is served by the host's icon store, uses no UI resource quota and stays valid across host
		/// restarts. Unknown id: <c>ICON_NOT_FOUND</c>.</summary>
		public const string GetIcon = "get-icon";

		public static readonly IReadOnlyList<string> All = [SyncBundled, GetIconResource, GetIcon];
	}

	public static class Devices
	{
		public const string Register = "register";

		public const string Update = "update";

		public const string Presence = "presence";

		public const string Unregister = "unregister";

		/// <summary>Reports a hardware interaction from an open device session.</summary>
		public const string Interaction = "interaction";

		/// <summary>Fetches icon bytes referenced by a device's current surface, over the <c>host.asset.*</c>
		/// pipeline.</summary>
		public const string Icon = "icon";

		/// <summary>Fetches the bytes behind a widget's currently rendered action-icon-provider icon, over
		/// the <c>host.asset.*</c> pipeline - see <c>MacroDeck.Sdk.Devices.DeviceSurfaceAppearance.HasProviderIcon</c>.
		/// Additive - a v1 plugin can serve a provider-controlled widget exactly as it can be a device
		/// provider at all.</summary>
		public const string WidgetIcon = "widget-icon";

		/// <summary>Closes an open device session at the provider's request, so a provider that stops
		/// serving a device is not left being pushed to.</summary>
		public const string Close = "close";

		public static readonly IReadOnlyList<string> All =
			[Register, Update, Presence, Unregister, Interaction, Icon, WidgetIcon, Close];
	}

	/// <summary>
	/// The <c>variable-values</c> host api: a push-capable variable provider publishes readings for the
	/// resources the host subscribed to, and tells the host when its catalog has changed.
	/// </summary>
	public static class VariableValues
	{
		public const string Value = "value";

		public const string Invalidate = "invalidate";

		public static readonly IReadOnlyList<string> All = [Value, Invalidate];
	}

	/// <summary>
	/// The <c>layouts</c> host api: a layout provider registers and withdraws the surfaces it describes.
	/// </summary>
	public static class Layouts
	{
		public const string Register = "register";

		public const string Unregister = "unregister";

		public static readonly IReadOnlyList<string> All = [Register, Unregister];
	}

	/// <summary>
	/// The <c>folder-views</c> host api: a folder view provider registers and withdraws the folder
	/// renderings it offers.
	/// </summary>
	public static class FolderViews
	{
		public const string Register = "register";

		public const string Unregister = "unregister";

		public static readonly IReadOnlyList<string> All = [Register, Unregister];
	}

	/// <summary>
	/// The <c>screensavers</c> host api: a screensaver provider registers and withdraws the screensavers it
	/// offers.
	/// </summary>
	public static class ScreenSavers
	{
		public const string Register = "register";

		public const string Unregister = "unregister";

		public static readonly IReadOnlyList<string> All = [Register, Unregister];
	}

	/// <summary>
	/// The <c>widget-types</c> host api: a widget type provider registers and withdraws the widget types
	/// it offers.
	/// </summary>
	public static class WidgetTypes
	{
		public const string Register = "register";

		public const string Unregister = "unregister";

		public static readonly IReadOnlyList<string> All = [Register, Unregister];
	}

	/// <summary>The <c>adb</c> host api. Every operation names the device by serial.</summary>
	public static class Adb
	{
		public const string Shell = "shell";

		public const string Battery = "battery";

		public const string Push = "push";

		public const string Pull = "pull";

		public const string Install = "install";

		public const string Uninstall = "uninstall";

		public const string PackageInstalled = "package-installed";

		/// <summary>Connects the host's adb to a device over the network. Takes no serial.</summary>
		public const string Connect = "connect";

		/// <summary>Presses one key of <see cref="AdbKeys" />. Allowed while the host is locked, like every typed
		/// operation below.</summary>
		public const string Key = "key";

		public const string Tap = "tap";

		public const string Swipe = "swipe";

		public const string InputText = "input-text";

		public const string StartApp = "start-app";

		public const string ForceStopApp = "force-stop-app";

		public const string OpenUri = "open-uri";

		public const string Reboot = "reboot";

		/// <summary>Answers <see cref="AdbScreenshotResultDto" />; the PNG follows over <c>host.asset.*</c>.</summary>
		public const string Screenshot = "screenshot";

		/// <summary>Answers <see cref="AdbDeviceStatusDto" />, or no data while the host has no reading yet.</summary>
		public const string Status = "status";

		public static readonly IReadOnlyList<string> All =
		[
			Shell, Battery, Push, Pull, Install, Uninstall, PackageInstalled, Connect, Key, Tap, Swipe, InputText,
			StartApp, ForceStopApp, OpenUri, Reboot, Screenshot, Status
		];
	}

	/// <summary>The <c>messaging</c> host api.</summary>
	public static class Messaging
	{
		/// <summary>Publishes an event to every matching subscription. Answers once the host accepted it.</summary>
		public const string Publish = "publish";

		/// <summary>Sends a command to the topic's one handler. Answers once the handler finished.</summary>
		public const string Send = "send";

		/// <summary>Sends a request to the topic's one handler and answers with its reply.</summary>
		public const string Request = "request";

		/// <summary>Replaces this plugin's event subscriptions and handled command and request topics.</summary>
		public const string Subscriptions = "subscriptions";

		public static readonly IReadOnlyList<string> All = [Publish, Send, Request, Subscriptions];
	}

	/// <summary>
	/// The <c>video-streams</c> host api. The host is the only writer of a plugin's provider table:
	/// <c>providers-changed</c> and <c>streams-changed</c> carry no catalog, they make the host re-read it
	/// through the <c>video-stream-provider</c> capability.
	/// </summary>
	public static class VideoStreams
	{
		public const string ProvidersChanged = "providers-changed";

		public const string StreamsChanged = "streams-changed";

		public const string SessionUpdate = "session-update";

		public const string SessionClose = "session-close";

		public static readonly IReadOnlyList<string> All =
			[ProvidersChanged, StreamsChanged, SessionUpdate, SessionClose];
	}

	public static class Colors
	{
		/// <summary>Resolves one colour value and answers with the colour, or none.</summary>
		public const string Resolve = "resolve";

		/// <summary>Replaces this plugin's whole table of watched colours. Idempotent.</summary>
		public const string Watches = "watches";

		public static readonly IReadOnlyList<string> All = [Resolve, Watches];
	}

	/// <summary>
	/// The <c>batteries</c> host api. The owner of every device is the calling plugin, never a payload
	/// field. Every operation is all or nothing: one invalid device refuses the whole call with
	/// <c>INVALID_PAYLOAD</c> and nothing is applied.
	/// </summary>
	public static class Batteries
	{
		/// <summary>Adds or replaces up to <c>ProtocolLimits.MaxBatteryDevicesPerProvider</c> devices.</summary>
		public const string Publish = "publish";

		/// <summary>Removes devices for good; readers see their ids in <c>RemovedIds</c>.</summary>
		public const string Remove = "remove";

		/// <summary>Replaces the plugin's whole set of devices. Devices left out count as removed.</summary>
		public const string Sync = "sync";

		/// <summary>
		/// Withdraws every device this plugin session published, without arguments. Readers see them
		/// disappear but not in <c>RemovedIds</c>, so values derived from them, such as battery variables, are
		/// kept as not connected instead of being deleted. A provider sends it before it is initialized again
		/// within the same session.
		/// </summary>
		public const string Withdraw = "withdraw";

		public static readonly IReadOnlyList<string> All = [Publish, Remove, Sync, Withdraw];
	}

	public static class Weather
	{
		/// <summary>
		/// Without arguments: the plugin's station snapshots changed, read them again now. Notices that arrive
		/// while a read is pending are merged into it.
		/// </summary>
		public const string SnapshotsChanged = "snapshots-changed";

		public static readonly IReadOnlyList<string> All = [SnapshotsChanged];
	}

	/// <summary>The <c>locale</c> host api.</summary>
	public static class Locale
	{
		/// <summary>Takes no arguments and answers a <see cref="LocaleTimeOfDayFormatResult" />: the culture of
		/// the Macro Deck interface and the hour cycle the user chose, or the operating system's.</summary>
		public const string TimeOfDayFormat = "time-of-day-format";

		public static readonly IReadOnlyList<string> All = [TimeOfDayFormat];
	}

	/// <summary>The <c>http</c> host api is push-only, so it declares no operations.</summary>
	public static class Http
	{
		public static readonly IReadOnlyList<string> All = [];
	}

	/// <summary>The <c>companion-devices</c> host api. Every operation is refused with
	/// <c>COMPANION_DEVICES_NOT_ALLOWED</c> while the plugin's access is not <c>available</c>.</summary>
	public static class CompanionDevices
	{
		/// <summary>Waits up to a few seconds for a device that has just connected to report its state, see
		/// <see cref="CompanionDeviceArguments" />, and answers <see cref="CompanionDeviceWaitResultDto" />.</summary>
		public const string Wait = "wait";

		/// <summary>Sends a command without waiting for the device, see <see cref="CompanionDeviceCommandArguments" />,
		/// and answers <see cref="CompanionDeviceSendResultDto" />.</summary>
		public const string Send = "send";

		/// <summary>Sends a command and waits for the device's answer, up to about two minutes, and answers
		/// <see cref="CompanionDeviceRequestResultDto" />. Cancelling it withdraws a command the device has not
		/// accepted yet.</summary>
		public const string Request = "request";

		/// <summary>Captures the screen, see <see cref="CompanionScreenshotArguments" />, and answers
		/// <see cref="CompanionScreenshotResultDto" />; the PNG follows over <c>host.asset.*</c>.</summary>
		public const string Screenshot = "screenshot";

		public static readonly IReadOnlyList<string> All = [Wait, Send, Request, Screenshot];
	}

	/// <summary>The <c>music-players</c> host api.</summary>
	public static class MusicPlayers
	{
		/// <summary>Answers <see cref="MusicPlayerAlbumColorResult" /> for <see cref="MusicPlayerAlbumColorArguments" />:
		/// the average colour of the player's cover, or the last one the host derived for it. The host may have to
		/// fetch the cover from its provider first, so the call can take a few seconds.</summary>
		public const string AlbumColor = "album-color";

		public static readonly IReadOnlyList<string> All = [AlbumColor];
	}

	/// <summary>The <c>desktop-notifications</c> host api. Every operation is refused with
	/// <c>DESKTOP_NOTIFICATIONS_NOT_ALLOWED</c> unless the plugin may show desktop notifications.</summary>
	public static class DesktopNotifications
	{
		/// <summary>Shows a notification, see <see cref="DesktopNotificationShowArguments" />, and answers no data once
		/// Macro Deck has handed it to the operating system. A computer without desktop notifications shows
		/// nothing and still answers.</summary>
		public const string Show = "show";

		public static readonly IReadOnlyList<string> All = [Show];
	}

	/// <summary>The <c>event-bindings</c> host api is push-only, so it declares no operations.</summary>
	public static class EventBindings
	{
		public static readonly IReadOnlyList<string> All = [];
	}

	private static readonly Dictionary<string, IReadOnlyList<string>> _byApi =
		new(StringComparer.Ordinal)
		{
			[HostApis.Variables] = Variables.All,
			[HostApis.UserVariables] = UserVariables.All,
			[HostApis.Config] = Config.All,
			[HostApis.Deck] = Deck.All,
			[HostApis.Scripts] = Scripts.All,
			[HostApis.Widgets] = Widgets.All,
			[HostApis.Notifications] = Notifications.All,
			[HostApis.ActionInteractions] = ActionInteractions.All,
			[HostApis.Ui] = Ui.All,
			[HostApis.Devices] = Devices.All,
			[HostApis.VariableValues] = VariableValues.All,
			[HostApis.Layouts] = Layouts.All,
			[HostApis.FolderViews] = FolderViews.All,
			[HostApis.ScreenSavers] = ScreenSavers.All,
			[HostApis.WidgetTypes] = WidgetTypes.All,
			[HostApis.EventBindings] = EventBindings.All,
			[HostApis.Adb] = Adb.All,
			[HostApis.Messaging] = Messaging.All,
			[HostApis.IconPacks] = IconPacks.All,
			[HostApis.VideoStreams] = VideoStreams.All,
			[HostApis.Colors] = Colors.All,
			[HostApis.Batteries] = Batteries.All,
			[HostApis.Weather] = Weather.All,
			[HostApis.Locale] = Locale.All,
			[HostApis.Http] = Http.All,
			[HostApis.CompanionDevices] = CompanionDevices.All,
			[HostApis.MusicPlayers] = MusicPlayers.All,
			[HostApis.DesktopNotifications] = DesktopNotifications.All
		};

	public static IReadOnlyList<string> For(string api)
	{
		return _byApi[api];
	}

	public static bool IsKnown(string? api, string? operation)
	{
		return api is not null
			&& operation is not null
			&& _byApi.TryGetValue(api, out var operations)
			&& operations.Contains(operation, StringComparer.Ordinal);
	}
}
