namespace MacroDeck.Sdk.Batteries;

/// <summary>
/// Implemented by integrations that report battery levels of devices - phones, headphones, controllers
/// and the like. The provider polls or listens however its devices require and publishes what it finds
/// through <see cref="IBatteryProviderContext" />; Macro Deck makes the devices available to widgets,
/// variables and every plugin's <see cref="IIntegrationContext.Batteries" />.
/// </summary>
/// <remarks>
/// Macro Deck calls <see cref="InitializeAsync" /> after the integration itself has initialized, and
/// <see cref="ShutdownAsync" /> before it stops. When the provider stops, its devices are withdrawn from
/// readers without counting as removed, and it publishes them again on its next initialization. A Macro
/// Deck that predates battery providers ignores what the provider publishes.
/// </remarks>
public interface IBatteryProvider
{
	/// <summary>Human-readable provider name. Optional: the default empty name stands for the integration's own name.</summary>
	string ProviderName => string.Empty;

	/// <summary>Starts reporting. Publish what is already known, then keep publishing changes.</summary>
	Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default);

	/// <summary>Stops reporting and releases whatever <see cref="InitializeAsync" /> acquired.</summary>
	Task ShutdownAsync(CancellationToken cancellationToken = default);
}
