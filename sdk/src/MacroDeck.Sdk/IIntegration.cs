using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeck.Sdk;

/// <summary>A pluggable integration with actions and optional capabilities.</summary>
public interface IIntegration
{
	string Id { get; }

	LocalizedText Name { get; }

	string Version { get; }

	/// <summary>
	/// Actions exposed by the integration. The collection must be complete and side-effect free before initialization.
	/// </summary>
	IReadOnlyList<IActionDefinition> Actions { get; }

	/// <summary>Initializes the integration and starts any required background work.</summary>
	Task InitializeAsync(IIntegrationContext context);

	/// <summary>Stops the integration and releases resources acquired during initialization.</summary>
	Task ShutdownAsync();

	/// <summary>Whether initialization completed and the integration is currently usable.</summary>
	bool IsInitialized { get; }
}
