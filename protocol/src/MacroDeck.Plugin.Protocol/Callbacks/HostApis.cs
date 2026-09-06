namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>The host APIs a plugin can reach through <c>host.invoke</c>. <c>events</c> is deliberately
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

	public static readonly IReadOnlyList<string> All =
	[
		Variables, UserVariables, Config, Deck, Scripts, Widgets, Notifications, ActionInteractions, Ui,
		Devices, VariableValues, Layouts, FolderViews, WidgetTypes,
	];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? api) => api is not null && _known.Contains(api);
}
