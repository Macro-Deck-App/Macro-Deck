using System.Collections.Immutable;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The interfaces a plugin or integration author implements to plug into the SDK - what MDP3002 and
/// MDP3003 mean by "an SDK contract". Deliberately the interfaces an author's own type implements, not
/// every type the SDK ships.
/// </summary>
internal static class SdkContractTypeNames
{
	public static readonly ImmutableArray<string> All =
	[
		WellKnownTypeNames.CapabilityHandler,
		WellKnownTypeNames.ActionExecutor,
		WellKnownTypeNames.ConfigFlow,
		WellKnownTypeNames.Integration,
		WellKnownTypeNames.PluginIntegration,
		WellKnownTypeNames.ActionDefinition,
		"MacroDeck.Sdk.ConfigFlow.IConfigFlowProvider",
		"MacroDeck.Sdk.Variables.IVariableProvider",
		"MacroDeck.Sdk.Events.IEventProvider",
		"MacroDeck.Sdk.Weather.IWeatherProvider",
		"MacroDeck.Sdk.MusicPlayer.IMusicPlayerProvider",
		"MacroDeck.Sdk.Profiles.IProfileProvider",
		"MacroDeck.Sdk.Issues.IIntegrationIssueProvider",
		"MacroDeck.Sdk.IIntegrationIconProvider",
		"MacroDeck.Plugin.Hosting.IPluginStartup"
	];
}
