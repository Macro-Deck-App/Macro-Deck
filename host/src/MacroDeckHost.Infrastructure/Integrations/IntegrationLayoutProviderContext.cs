using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Binds one in-process integration to the layout registry. The integration id is captured here, so a
/// provider can only ever register layouts in its own name.
/// </summary>
internal sealed class IntegrationLayoutProviderContext : ILayoutProviderContext
{
	private readonly string _integrationId;
	private readonly ILayoutRegistry _registry;

	public IntegrationLayoutProviderContext(string integrationId, ILayoutRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task<LayoutRegistration> RegisterLayoutAsync(
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default)
		=> _registry.Register(_integrationId, layout, cancellationToken);

	public Task UnregisterLayoutAsync(string layoutId, CancellationToken cancellationToken = default)
		=> _registry.Unregister(_integrationId, layoutId, cancellationToken);
}
