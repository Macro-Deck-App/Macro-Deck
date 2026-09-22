namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>The eighteen capability kinds a plugin can declare.</summary>
public static class CapabilityKinds
{
	public const string Actions = "actions";

	public const string Events = "events";

	public const string Variables = "variables";

	public const string Icons = "icons";

	public const string ConfigFlow = "config-flow";

	public const string MusicPlayer = "music-player";

	public const string Weather = "weather";

	public const string VirtualProfiles = "virtual-profiles";

	public const string Issues = "issues";

	public const string Ui = "ui";

	public const string Localization = "localization";

	public const string DeviceProvider = "device-provider";

	public const string LayoutProvider = "layout-provider";

	public const string FolderViewProvider = "folder-view-provider";

	/// <summary>Takes this plugin's own actions and settings over from another application (issue #365).</summary>
	public const string Migration = "migration";

	/// <summary>Offers deck widget types of this plugin's own (issue #843).</summary>
	public const string WidgetTypeProvider = "widget-type-provider";

	/// <summary>Offers screensavers a device shows after sitting idle.</summary>
	public const string ScreenSaverProvider = "screensaver-provider";

	/// <summary>
	/// Receives events, commands and requests other plugins and integrations address to a topic this
	/// plugin subscribed to or handles. Declared at <see cref="Capabilities.ProviderCapabilityId.LocalId" />.
	/// </summary>
	public const string Messaging = "messaging";

	public static readonly IReadOnlyList<string> All =
	[
		Actions, Events, Variables, Icons, ConfigFlow, MusicPlayer, Weather, VirtualProfiles, Issues, Ui,
		Localization, DeviceProvider, LayoutProvider, FolderViewProvider, Migration, WidgetTypeProvider,
		ScreenSaverProvider, Messaging,
	];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? kind) => kind is not null && _known.Contains(kind);
}
