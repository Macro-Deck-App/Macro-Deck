namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Fully qualified metadata names for the SDK and BCL types these analyzers reason about.
///
/// <para>
/// None of these types are referenced as an assembly - an analyzer runs inside the compiler process
/// analyzing a different compilation than the one it ships in, so it can only ever look a type up by
/// name against whatever compilation it is handed (<c>Compilation.GetTypeByMetadataName</c>) and get
/// back <c>null</c> when that compilation does not reference it. Centralized so a rename on the SDK side
/// only has to be caught in one place - the "used by no analyzer" state that leaves is a broken build,
/// not a silently inert rule.
/// </para>
/// </summary>
internal static class WellKnownTypeNames
{
	public const string CapabilityHandler = "MacroDeck.Plugin.Hosting.Capabilities.ICapabilityHandler";

	/// <summary>As a display string, since this is compared against a property's declared type.</summary>
	public const string LocalizedTextDisplay = "MacroDeck.Localization.LocalizedText";

	public const string CapabilityInvocationContext =
		"MacroDeck.Plugin.Hosting.Capabilities.ICapabilityInvocationContext";

	public const string DeclaredCapability = "MacroDeck.Plugin.Protocol.Handshake.DeclaredCapability";

	public const string Integration = "MacroDeck.Sdk.IIntegration";

	/// <summary>The out-of-process plugin author's integration contract - see that interface's own
	/// remarks for how it differs from <see cref="Integration" />, which only the in-process host still
	/// uses.</summary>
	public const string PluginIntegration = "MacroDeck.Sdk.IPluginIntegration";

	public const string PluginHostBuilder = "MacroDeck.Plugin.Hosting.PluginHostBuilder";

	public const string MacroDeckIntegrationAttribute = "MacroDeck.Sdk.MacroDeckIntegrationAttribute";

	public const string IntegrationIconProvider = "MacroDeck.Sdk.IIntegrationIconProvider";

	public const string ActionDefinition = "MacroDeck.Sdk.Actions.IActionDefinition";

	public const string ActionExecutor = "MacroDeck.Sdk.Actions.IActionExecutor";

	public const string ActionExecutionContext = "MacroDeck.Sdk.Actions.ActionExecutionContext";

	public const string ConfigFlow = "MacroDeck.Sdk.ConfigFlow.IConfigFlow";

	public const string MacroDeckServiceCollectionExtensions =
		"MacroDeck.Plugin.Hosting.DependencyInjection.MacroDeckServiceCollectionExtensions";

	public const string ServiceCollectionServiceExtensions =
		"Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";

	public const string ServiceCollectionDescriptorExtensions =
		"Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";

	public const string EndpointRouteBuilderExtensions = "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions";

	public const string EndpointRouteBuilder = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";

	public const string WebHostBuilder = "Microsoft.AspNetCore.Hosting.IWebHostBuilder";

	public const string Configuration = "Microsoft.Extensions.Configuration.IConfiguration";

	public const string CancellationToken = "System.Threading.CancellationToken";

	public const string Thread = "System.Threading.Thread";

	public const string Task = "System.Threading.Tasks.Task";

	public const string TaskOfT = "System.Threading.Tasks.Task`1";

	public const string ValueTaskOfT = "System.Threading.Tasks.ValueTask`1";

	public const string ObsoleteAttribute = "System.ObsoleteAttribute";

	public const string MacroDeckDeprecatedAttribute = "MacroDeck.Sdk.Deprecation.MacroDeckDeprecatedAttribute";

	public const string MacroDeckSdkUsageAttribute = "MacroDeck.Sdk.Deprecation.MacroDeckSdkUsageAttribute";

	public const string EventArgs = "System.EventArgs";
}
