using MacroDeck.Plugin.Protocol.Callbacks;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// The permission vocabulary a manifest may declare.
/// <para>
/// <b>Nothing in the host enforces these today.</b> They are parsed, shape-validated, persisted and
/// exposed so an artifact can disclose what it intends to reach, and so a consent surface has something
/// to render. Enforcement is a separate change: every plugin that exists now declares no permissions and
/// self-registering development plugins have no manifest at all, so default-deny would break shipped
/// behaviour while default-allow would not be a security boundary at all. See ADR 0029.
/// </para>
/// <para>
/// The <c>host:</c> entries mirror <see cref="HostApis"/> one for one, so the vocabulary cannot drift
/// away from the callback surface it describes.
/// </para>
/// </summary>
public static class PluginPermissions
{
	public const string HostVariables = "host:variables";

	public const string HostUserVariables = "host:user-variables";

	public const string HostConfig = "host:config";

	public const string HostDeck = "host:deck";

	public const string HostScripts = "host:scripts";

	public const string HostWidgets = "host:widgets";

	public const string HostNotifications = "host:notifications";

	public const string HostActionInteractions = "host:action-interactions";

	public const string HostDevices = "host:devices";

	public const string HostVariableValues = "host:variable-values";

	public const string HostLayouts = "host:layouts";

	public const string HostFolderViews = "host:folder-views";

	public const string HostWidgetTypes = "host:widget-types";

	public const string EventsPublish = "events:publish";

	public const string AssetsUpload = "assets:upload";

	public const string NetOutbound = "net:outbound";

	public const string FileSystemUserFiles = "fs:user-files";

	public const string ProcessSpawn = "process:spawn";

	public const string DeviceUsb = "device:usb";

	public static readonly IReadOnlyList<string> All =
	[
		HostVariables, HostUserVariables, HostConfig, HostDeck, HostScripts, HostWidgets,
		HostNotifications, HostActionInteractions, HostDevices, HostVariableValues, HostLayouts,
		HostFolderViews, HostWidgetTypes, EventsPublish, AssetsUpload, NetOutbound, FileSystemUserFiles,
		ProcessSpawn, DeviceUsb,
	];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	/// <summary>An unknown permission is reported, never fatal - the same forward-compatibility stance
	/// <see cref="HostApis.IsKnown"/> takes.</summary>
	public static bool IsKnown(string? permission)
	{
		return permission is not null && _known.Contains(permission);
	}

	/// <summary>The permission covering a given <see cref="HostApis"/> value, so the mapping lives in one
	/// place when enforcement lands.</summary>
	public static string ForHostApi(string hostApi)
	{
		return "host:" + hostApi;
	}
}
