using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Layouts;

/// <summary>
/// Describes the software client's own grid as a layout (issue #384). Existing client devices keep
/// <c>LayoutReference == null</c> - registering this changes nothing about them, only makes the layout
/// available for a device that opts into referencing it.
/// </summary>
[MacroDeckIntegration]
public sealed class ClientLayoutProviderIntegration : IIntegration, ISystemIntegration, ILayoutProvider
{
	public const string IntegrationId = "app.macro-deck.client-layout";

	public const string LayoutLocalId = "software-client";

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.ClientLayout.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions => [];

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public async Task InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken = default)
	{
		await context.RegisterLayoutAsync(BuildLayout(), cancellationToken);
	}

	public IReadOnlyList<LayoutDescriptor> GetLayouts() => [BuildLayout()];

	// LayoutDescriptor.Name is a plain string by contract - every provider, this host's own included,
	// supplies it already resolved. A built-in integration is instantiated by reflection with no DI
	// (see IntegrationDiscovery), so unlike IIntegration.Name above it cannot resolve a LocalizedText
	// against the active culture here; English is the descriptive, product-name-like label every other
	// provider's layout name already is (compare LayoutDescriptor's own "Stream Deck XL" example).
	private const string LayoutDisplayName = "Software Client";

	private static LayoutDescriptor BuildLayout()
		=> new(LayoutLocalId,
			LayoutDisplayName,
			[
				new LayoutRegion
				{
					Id = "grid",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = GridDefaults.Rows,
						Columns = GridDefaults.Columns,
						IsConfigurable = true,
						MinRows = GridDefaults.MinRows,
						MaxRows = GridDefaults.MaxRows,
						MinColumns = GridDefaults.MinColumns,
						MaxColumns = GridDefaults.MaxColumns,
						SupportsRuntimeResize = true,
						KeySize = null
					},
					Visuals = LayoutVisualCapabilities.Full
				}
			]);
}
