namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// The operation vocabulary for each <see cref="MacroDeck.Plugin.Protocol.Handshake.CapabilityKinds" />
/// member - what a <c>capability.invoke</c>'s <c>operation</c> field may say for that kind.
///
/// <para>
/// Two different localId conventions sit behind these operations. <c>actions</c> and <c>variables</c>
/// address a <em>declared</em> local id: one <c>DeclaredCapability</c> per item, validated by
/// <c>DeclaredIdValidator</c> - though <c>variables</c> also answers catalog operations whose resource
/// ids are too numerous to declare and travel inside the arguments instead, see <see cref="Variables" />.
/// Every other, provider-shaped kind - <c>music-player</c>, <c>weather</c>,
/// <c>virtual-profiles</c>, and so on - instead declares a single <c>provider</c> local id and carries
/// its runtime instance id inside the operation arguments, because those instance ids are config-entry
/// GUIDs that simply do not exist yet at declaration time. The host's <c>IntegrationCapabilityValidator</c>
/// draws this same line for in-process integrations; this is its wire-protocol counterpart.
/// </para>
/// </summary>
public static class CapabilityOperations
{
	public static class Actions
	{
		public const string Describe = "describe";

		public const string Execute = "execute";

		public const string Options = "options";

		/// <summary>
		/// The <c>actions</c> capability's state-provider read - additive within protocol v1, since a
		/// v1 plugin can be a state provider just as it can supply dynamic options. See
		/// <c>MacroDeck.Sdk.Actions.IStateProviderActionDefinition</c>.
		/// </summary>
		public const string State = "state";

		/// <summary>
		/// The <c>actions</c> capability's icon-provider poll: returns an identity, not bytes. See
		/// <c>MacroDeck.Sdk.Actions.IIconProviderActionDefinition.GetActionIconAsync</c>. Additive
		/// within protocol v1.
		/// </summary>
		public const string Icon = "icon";

		/// <summary>
		/// Fetches the bytes behind an <see cref="Icon" /> reply's identity, uploaded through the
		/// <c>asset.*</c> pipeline rather than carried in the reply - see
		/// <c>MacroDeck.Sdk.Actions.IIconProviderActionDefinition.GetActionIconContentAsync</c>.
		/// Additive within protocol v1.
		/// </summary>
		public const string IconContent = "icon.content";

		public static readonly IReadOnlyList<string> All =
			[Describe, Execute, Options, State, Icon, IconContent];
	}

	/// <summary>
	/// Both halves of a variable provider. <see cref="Get" /> and <see cref="Set" /> address one variable
	/// by the invoke payload's <c>localId</c>; <see cref="Describe" />, <see cref="Discover" />,
	/// <see cref="Resolve" /> and <see cref="Subscribe" /> ignore it and carry whatever they address
	/// inside their arguments.
	///
	/// <para>
	/// That split is why the kind declares one <c>DeclaredCapability</c> per eager variable <em>and</em>
	/// answers operations that name no declared id: a provider's catalog half may expose tens of thousands
	/// of resources, far too many to declare, so those ids are
	/// <c>Identity.LocalIdKind.Resource</c> and only ever appear inside operation arguments - while the
	/// eager half stays one declared <c>Identity.LocalIdKind.Declared</c> id per variable, exactly as
	/// <c>actions</c> does.
	/// </para>
	/// </summary>
	public static class Variables
	{
		public const string Describe = "describe";

		public const string Get = "get";

		/// <summary>Applies a value to one variable. Only ever invoked for a definition that declared a
		/// write capability - the host refuses the rest itself.</summary>
		public const string Set = "set";

		/// <summary>Returns one page of the provider's on-demand catalog.</summary>
		public const string Discover = "discover";

		/// <summary>Resolves a single catalog resource id that may never have come out of
		/// <see cref="Discover" />.</summary>
		public const string Resolve = "resolve";

		/// <summary>Declares the complete set of catalog resources the host currently cares about.</summary>
		public const string Subscribe = "subscribe";

		public static readonly IReadOnlyList<string> All =
			[Describe, Get, Set, Discover, Resolve, Subscribe];
	}

	public static class Events
	{
		public const string Describe = "describe";

		public const string Options = "options";

		public static readonly IReadOnlyList<string> All = [Describe, Options];
	}

	public static class Migration
	{
		/// <summary>Which applications this plugin migrates from, and what it claims in each.</summary>
		public const string Describe = "describe";

		/// <summary>Translates one foreign action, or answers that it has no equivalent.</summary>
		public const string MigrateAction = "migrate-action";

		/// <summary>Turns a foreign plugin's settings and credentials into configuration entries.</summary>
		public const string MigrateConfiguration = "migrate-configuration";

		public static readonly IReadOnlyList<string> All = [Describe, MigrateAction, MigrateConfiguration];
	}

	public static class Icons
	{
		public const string Describe = "describe";

		public static readonly IReadOnlyList<string> All = [Describe];
	}

	public static class ConfigFlow
	{
		public const string Describe = "describe";

		public const string FlowStart = "flow.start";

		public const string FlowSubmit = "flow.submit";

		public const string FlowAbandon = "flow.abandon";

		public static readonly IReadOnlyList<string> All = [Describe, FlowStart, FlowSubmit, FlowAbandon];
	}

	public static class MusicPlayer
	{
		public const string Describe = "describe";

		public const string Instances = "instances";

		public const string State = "state";

		public const string Artwork = "artwork";

		public const string Play = "play";

		public const string PlayItem = "play-item";

		public const string Pause = "pause";

		public const string Toggle = "toggle";

		public const string Next = "next";

		public const string Previous = "previous";

		public const string Seek = "seek";

		public const string Volume = "volume";

		public const string Shuffle = "shuffle";

		public const string Repeat = "repeat";

		public const string Catalog = "catalog";

		public const string Devices = "devices";

		public const string Transfer = "transfer";

		public static readonly IReadOnlyList<string> All =
		[
			Describe, Instances, State, Artwork, Play, PlayItem, Pause, Toggle, Next, Previous, Seek,
			Volume, Shuffle, Repeat, Catalog, Devices, Transfer,
		];
	}

	public static class Weather
	{
		public const string Describe = "describe";

		public const string Instances = "instances";

		public const string Snapshot = "snapshot";

		public static readonly IReadOnlyList<string> All = [Describe, Instances, Snapshot];
	}

	public static class VirtualProfiles
	{
		public const string Describe = "describe";

		public const string Profiles = "profiles";

		public const string WidgetInteraction = "widget-interaction";

		public static readonly IReadOnlyList<string> All = [Describe, Profiles, WidgetInteraction];
	}

	public static class Issues
	{
		public const string Describe = "describe";

		public const string List = "list";

		public const string Resolve = "resolve";

		public static readonly IReadOnlyList<string> All = [Describe, List, Resolve];
	}

	public static class Ui
	{
		public const string Describe = "describe";

		public const string SessionOpen = "session.open";

		public const string SessionClose = "session.close";

		public const string SessionSnapshot = "session.snapshot";

		public const string SessionEvent = "session.event";

		/// <summary>
		/// Delivers the outcome of a modal the plugin opened through
		/// <c>HostOperations.ActionInteractions.ShowModal</c>. Host-to-plugin because the wait is unbounded
		/// by a person: the opening call answers immediately, and this arrives whenever the user does.
		/// </summary>
		public const string ModalResult = "modal.result";

		public static readonly IReadOnlyList<string> All =
			[Describe, SessionOpen, SessionClose, SessionSnapshot, SessionEvent, ModalResult];
	}

	/// <summary>
	/// Localization declares which cultures a plugin ships and hands over one culture's catalog on
	/// request. Two operations rather than one payload pushed at declaration time: a catalog is bounded
	/// per culture but unbounded across them, and the host only ever needs the active language and its
	/// fallback.
	/// </summary>
	public static class Localization
	{
		public const string Describe = "describe";

		public const string Catalog = "catalog";

		public static readonly IReadOnlyList<string> All = [Describe, Catalog];
	}

	/// <summary>
	/// A device provider is driven from the plugin side - it registers, updates and unregisters devices
	/// through the <c>devices</c> host api - so the host-to-provider direction only has to describe the
	/// provider and re-read its catalog after a reconnect.
	/// </summary>
	public static class DeviceProvider
	{
		public const string Describe = "describe";

		public const string Devices = "devices";

		/// <summary>Opens a device session, handing the provider the deck surface to render.</summary>
		public const string SessionOpen = "session.open";

		/// <summary>Pushes a new surface to an already-open session.</summary>
		public const string SessionSurface = "session.surface";

		/// <summary>Closes an open device session.</summary>
		public const string SessionClose = "session.close";

		public static readonly IReadOnlyList<string> All =
			[Describe, Devices, SessionOpen, SessionSurface, SessionClose];
	}

	/// <summary>
	/// A layout provider is driven from the plugin side - it registers and withdraws layouts through the
	/// <c>layouts</c> host api - so the host-to-provider direction only has to describe the provider and
	/// re-read its catalog after a reconnect.
	/// </summary>
	public static class LayoutProvider
	{
		public const string Describe = "describe";

		public const string Layouts = "layouts";

		public static readonly IReadOnlyList<string> All = [Describe, Layouts];
	}

	/// <summary>
	/// A folder view provider is driven from the plugin side - it registers and withdraws views through the
	/// <c>folder-views</c> host api - so the host-to-provider direction only has to describe the provider
	/// and re-read its catalog after a reconnect. The views themselves are served over the <c>ui</c>
	/// capability, like every other Macro Deck UI surface.
	/// </summary>
	public static class FolderViewProvider
	{
		public const string Describe = "describe";

		public const string FolderViews = "folder-views";

		public static readonly IReadOnlyList<string> All = [Describe, FolderViews];
	}

	/// <summary>
	/// A widget type provider is driven from the plugin side - it registers and withdraws types through
	/// the <c>widget-types</c> host api - so the host-to-provider direction only has to describe the
	/// provider and re-read its catalog after a reconnect. The widgets themselves are served over the
	/// <c>ui</c> capability, like every other Macro Deck UI surface.
	/// </summary>
	public static class WidgetTypeProvider
	{
		public const string Describe = "describe";

		public const string WidgetTypes = "widget-types";

		public static readonly IReadOnlyList<string> All = [Describe, WidgetTypes];
	}

	private static readonly Dictionary<string, IReadOnlyList<string>> _byKind =
		new(StringComparer.Ordinal)
		{
			[Handshake.CapabilityKinds.Actions] = Actions.All,
			[Handshake.CapabilityKinds.Variables] = Variables.All,
			[Handshake.CapabilityKinds.Events] = Events.All,
			[Handshake.CapabilityKinds.Icons] = Icons.All,
			[Handshake.CapabilityKinds.Migration] = Migration.All,
			[Handshake.CapabilityKinds.ConfigFlow] = ConfigFlow.All,
			[Handshake.CapabilityKinds.MusicPlayer] = MusicPlayer.All,
			[Handshake.CapabilityKinds.Weather] = Weather.All,
			[Handshake.CapabilityKinds.VirtualProfiles] = VirtualProfiles.All,
			[Handshake.CapabilityKinds.Issues] = Issues.All,
			[Handshake.CapabilityKinds.Ui] = Ui.All,
			[Handshake.CapabilityKinds.Localization] = Localization.All,
			[Handshake.CapabilityKinds.DeviceProvider] = DeviceProvider.All,
			[Handshake.CapabilityKinds.LayoutProvider] = LayoutProvider.All,
			[Handshake.CapabilityKinds.FolderViewProvider] = FolderViewProvider.All,
			[Handshake.CapabilityKinds.WidgetTypeProvider] = WidgetTypeProvider.All,
		};

	public static IReadOnlyList<string> For(string kind) => _byKind[kind];

	public static bool IsKnown(string? kind, string? operation)
		=> kind is not null &&
			operation is not null &&
			_byKind.TryGetValue(kind, out var operations) &&
			operations.Contains(operation, StringComparer.Ordinal);
}
