using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Integrations;

public static class ProvidedCapabilityCatalog
{
	/// <summary>
	/// Not a protocol capability kind: state provision is a per-action flag on an action descriptor, not
	/// something a plugin declares in its handshake. It is listed beside the declared kinds only because
	/// the integration pages present it as one more thing an integration can offer.
	/// </summary>
	public const string ActionStatesKind = "action-states";

	/// <summary>
	/// The icon-provider counterpart to <see cref="ActionStatesKind" />: also a per-action flag, not a
	/// declared protocol capability, and present under the same reasoning.
	/// </summary>
	public const string ActionIconsKind = "action-icons";

	private static readonly (string Kind, LocalizedText Name, Func<IIntegration, bool> Present)[] _entries =
	[
		(CapabilityKinds.Actions, AppStrings.Integrations.Capability.Actions(),
			integration => integration.Actions.Any(action => action.RunsHere())),
		(CapabilityKinds.Events, AppStrings.Integrations.Capability.Events(),
			integration => integration is IEventProvider),
		(CapabilityKinds.Variables, AppStrings.Integrations.Capability.Variables(),
			integration => integration is IVariableProvider),
		(CapabilityKinds.MusicPlayer, AppStrings.Integrations.Capability.MusicPlayer(),
			integration => integration is IMusicPlayerProvider),
		(CapabilityKinds.Weather, AppStrings.Integrations.Capability.Weather(),
			integration => integration is IWeatherProvider),
		(CapabilityKinds.VirtualProfiles, AppStrings.Integrations.Capability.VirtualProfiles(),
			integration => integration is IProfileProvider),
		(CapabilityKinds.DeviceProvider, AppStrings.Integrations.Capability.DeviceProvider(),
			integration => integration is IDeviceProvider),
		(CapabilityKinds.LayoutProvider, AppStrings.Integrations.Capability.LayoutProvider(),
			integration => integration is ILayoutProvider),
		(CapabilityKinds.FolderViewProvider, AppStrings.Integrations.Capability.FolderViewProvider(),
			integration => integration is IFolderViewProvider),
		(CapabilityKinds.WidgetTypeProvider, AppStrings.Integrations.Capability.WidgetTypeProvider(),
			integration => integration is IWidgetTypeProvider),

		// The only row that asks for more than the interface: every remote adapter implements
		// IMigrationProvider unconditionally, and a built-in that declares no migration has nothing to
		// offer either - what is worth showing is that this integration can take a setup over from
		// somewhere, which is exactly "the list is not empty".
		(CapabilityKinds.Migration, AppStrings.Integrations.Capability.Migration(),
			integration => integration is IMigrationProvider { Migrations.Count: > 0 })
	];

	public static IReadOnlyList<ProvidedCapability> For(IIntegration integration)
	{
		// A RemotePluginIntegration implements every provider interface unconditionally (ADR 0004),
		// so `is` alone cannot tell "declared" from "adapter shape" for it. IDeclaredCapabilityKinds is
		// the honest per-plugin signal instead. An empty declared list yields no capabilities here -
		// "unknown" is the honest answer, since falling back to the cast would light up all of them. That
		// is reachable for an installed-but-stopped plugin whose persisted snapshot predates
		// AcceptedKinds; it self-heals on the plugin's next successful connect.
		var declared = integration as IDeclaredCapabilityKinds;

		var capabilities = new List<ProvidedCapability>();

		foreach (var (kind, name, present) in _entries)
		{
			// Actions follows the same branch as every other row: RunsHere() for a built-in, the
			// negotiated set for a declared-kinds integration. RemoteActionDefinition never overrides
			// Platforms, so the two cannot currently disagree even though only one is ever consulted.
			var included = declared is not null
				? declared.DeclaredCapabilityKinds.Contains(kind, StringComparer.Ordinal)
				: present(integration);

			if (included)
			{
				capabilities.Add(new ProvidedCapability(kind, name));
			}
		}

		// Derived from the actions rather than from the declared kinds, which is what makes it right for a
		// plugin too: a remote action definition implements IStateProviderActionDefinition exactly when its
		// descriptor advertises the capability, so built-in and remote answer through the same question.
		if (integration.Actions.Any(action => action.RunsHere() && action is IStateProviderActionDefinition))
		{
			capabilities.Add(new ProvidedCapability(ActionStatesKind,
				AppStrings.Integrations.Capability.ActionStates()));
		}

		// Same reasoning as ActionStatesKind above, for icon provision instead - except a remote adapter
		// never implements IIconProviderActionDefinition directly, per ADR 0004/0056's closed eight-leaf
		// family (see RemoteActionDefinition.ProvidesIcon's own remarks), so this reads the same flag
		// GetActionsRequestMessageHandler does rather than testing the adapter against that interface.
		if (integration.Actions.Any(action => action.RunsHere() && IsIconProviderAction(action)))
		{
			capabilities.Add(new ProvidedCapability(ActionIconsKind,
				AppStrings.Integrations.Capability.ActionIcons()));
		}

		return capabilities;
	}

	private static bool IsIconProviderAction(IActionDefinition action) => action switch
	{
		RemoteActionDefinition remote => remote.ProvidesIcon,
		_ => action is IIconProviderActionDefinition
	};
}
