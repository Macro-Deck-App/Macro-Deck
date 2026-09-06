using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Migration;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed record RemotePluginCapabilitySnapshot
{
	public static RemotePluginCapabilitySnapshot Empty(string pluginId) => new() { PluginId = pluginId };

	public required string PluginId { get; init; }

	public IReadOnlyList<string> AcceptedKinds { get; init; } = [];

	public IReadOnlyList<RemoteActionDescriptor> Actions { get; init; } = [];

	public byte[] IconBytes { get; init; } = [];

	public string IconMimeType { get; init; } = "application/octet-stream";

	public bool HasIcon { get; init; }

	public string IconContentHash { get; init; } = string.Empty;

	public string IconBytesContentHash { get; init; } = string.Empty;

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; init; } = [];

	public IReadOnlyList<VariableDefinition> Variables { get; init; } = [];

	public bool VariablesDependOnConfiguration { get; init; }

	public string EventProviderName { get; init; } = string.Empty;

	public IReadOnlyList<EventDefinition> EventDefinitions { get; init; } = [];

	public bool HasDynamicEventOptions { get; init; }

	public string MusicPlayerProviderName { get; init; } = string.Empty;

	public IReadOnlyList<MusicPlayerInstance> MusicPlayerInstances { get; init; } = [];

	public IReadOnlyList<string> MusicPlayerCatalogInstanceIds { get; init; } = [];

	public IReadOnlyList<string> MusicPlayerDeviceInstanceIds { get; init; } = [];

	public string WeatherProviderName { get; init; } = string.Empty;

	public IReadOnlyList<WeatherStationInstance> WeatherInstances { get; init; } = [];

	public string ProfileProviderName { get; init; } = string.Empty;

	public IReadOnlyList<VirtualProfileDescriptor> Profiles { get; init; } = [];

	public bool AllowsMultipleConfigurations { get; init; } = true;

	public bool ServesConfigUiTree { get; init; }

	public IReadOnlyList<RemoteUiSurfaceDescriptor> UiSurfaces { get; init; } = [];

	/// <summary>
	/// What this plugin declared it can take a setup over from. Empty for a plugin that declared no
	/// migration capability, which reads the same as declaring one with nothing in it - the distinction
	/// costs a consumer nothing, exactly as it does for the other provider-shaped capabilities.
	/// </summary>
	public IReadOnlyList<RemoteMigrationDescriptor> Migrations { get; init; } = [];

	public int UiModelVersion { get; init; }

	public IReadOnlyList<RemoteUiPreviewDescriptor> UiPreviews { get; init; } = [];

	// Only the catalog's shape is cached, never its contents: discovery over a provider's own resource
	// tree is deliberately never enumerated or persisted here - see IVariableProvider's remarks on why
	// the host never enumerates the whole set.
	public string VariableCatalogName { get; init; } = string.Empty;

	/// <summary>Total bindable catalog entries the provider reports, or null when it cannot say.</summary>
	public int? VariableCatalogEntryCount { get; init; }

	public bool SupportsVariableCatalog { get; init; }

	public bool SupportsVariablePush { get; init; }

	public bool SupportsVariableSearch { get; init; }
}
