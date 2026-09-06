using MacroDeck.Sdk;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Runs the layout-provider side of an in-process integration's lifecycle: hands a started provider its
/// context, and withdraws its layouts when it stops. Mirrors <see cref="DeviceProviderHost" /> exactly -
/// see its remarks for why process exit is not a call site here either.
/// </summary>
public sealed class LayoutProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly ILayoutRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public LayoutProviderHost(ILayoutRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<LayoutProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not ILayoutProvider provider)
		{
			return;
		}

		var context = new IntegrationLayoutProviderContext(integration.Id, _registry);

		try
		{
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Layout provider '{IntegrationId}' failed to start and provides no layouts",
				integration.Id);
		}
	}

	/// <summary>
	/// Withdraws the integration's layouts from the live registry. This does not unconstrain any
	/// profile a device built against them - the constraint lives in each device's persisted
	/// <c>LayoutSnapshot</c>, which a provider merely stopping never touches.
	/// </summary>
	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is ILayoutProvider)
		{
			await _registry.UnregisterAll(integration.Id, cancellationToken);
		}
	}
}
