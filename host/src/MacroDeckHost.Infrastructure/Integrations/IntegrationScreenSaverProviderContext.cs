using MacroDeck.Sdk.ScreenSavers;
using MacroDeckHost.Application.ScreenSavers;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class IntegrationScreenSaverProviderContext : IScreenSaverProviderContext
{
	private readonly string _integrationId;
	private readonly IScreenSaverRegistry _registry;

	public IntegrationScreenSaverProviderContext(string integrationId, IScreenSaverRegistry registry)
	{
		_integrationId = integrationId;
		_registry = registry;
	}

	public Task<ScreenSaverRegistration> RegisterScreenSaverAsync(
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default)
		=> _registry.Register(_integrationId, screenSaver, cancellationToken);

	public Task UnregisterScreenSaverAsync(string screenSaverId, CancellationToken cancellationToken = default)
		=> _registry.Unregister(_integrationId, screenSaverId, cancellationToken);
}
