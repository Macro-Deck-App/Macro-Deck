using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Binds one in-process integration to the widget type registry. The integration id is captured here, so
/// a provider can only ever register types in its own name.
/// </summary>
internal sealed class IntegrationWidgetTypeProviderContext : IWidgetTypeProviderContext
{
	private readonly string _integrationId;
	private readonly IWidgetTypeRegistry _registry;

	public IntegrationWidgetTypeProviderContext(string integrationId, IWidgetTypeRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default)
		=> _registry.Register(_integrationId, widgetType, cancellationToken);

	public Task UnregisterWidgetTypeAsync(string widgetTypeId, CancellationToken cancellationToken = default)
		=> _registry.Unregister(_integrationId, widgetTypeId, cancellationToken);
}
