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

		public static readonly IReadOnlyList<string> All = [List, Get, Create, Set, Delete];
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

		public static readonly IReadOnlyList<string> All = [Apply, InvalidateIcon];
	}

	public static class Notifications
	{
		public const string Notify = "notify";

		public const string Dismiss = "dismiss";

		public static readonly IReadOnlyList<string> All = [Notify, Dismiss];
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

		public static readonly IReadOnlyList<string> All = [Snapshot, Patch, Fault];
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
	/// The <c>widget-types</c> host api: a widget type provider registers and withdraws the widget types
	/// it offers.
	/// </summary>
	public static class WidgetTypes
	{
		public const string Register = "register";

		public const string Unregister = "unregister";

		public static readonly IReadOnlyList<string> All = [Register, Unregister];
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
			[HostApis.WidgetTypes] = WidgetTypes.All,
		};

	public static IReadOnlyList<string> For(string api) => _byApi[api];

	public static bool IsKnown(string? api, string? operation)
		=> api is not null &&
			operation is not null &&
			_byApi.TryGetValue(api, out var operations) &&
			operations.Contains(operation, StringComparer.Ordinal);
}
