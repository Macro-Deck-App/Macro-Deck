using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public class IntegrationLifecycle : IIntegrationLifecycle
{
	private readonly IIntegrationRegistry _registry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IMediator _mediator;
	private readonly IPluginSessionRegistry _pluginSessionRegistry;
	private readonly IntegrationInitializer _initializer;
	private readonly LayoutProviderHost _layoutProviders;
	private readonly FolderViewProviderHost _folderViewProviders;
	private readonly WidgetTypeProviderHost _widgetTypeProviders;
	private readonly DeviceProviderHost _deviceProviders;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public IntegrationLifecycle(
		IIntegrationRegistry registry,
		IServiceScopeFactory scopeFactory,
		IMediator mediator,
		IPluginSessionRegistry pluginSessionRegistry,
		IntegrationInitializer initializer,
		LayoutProviderHost layoutProviders,
		FolderViewProviderHost folderViewProviders,
		WidgetTypeProviderHost widgetTypeProviders,
		DeviceProviderHost deviceProviders,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_registry = registry;
		_scopeFactory = scopeFactory;
		_mediator = mediator;
		_pluginSessionRegistry = pluginSessionRegistry;
		_initializer = initializer;
		_layoutProviders = layoutProviders;
		_folderViewProviders = folderViewProviders;
		_widgetTypeProviders = widgetTypeProviders;
		_deviceProviders = deviceProviders;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default)
	{
		var integration = _registry.Integrations.FirstOrDefault(i => i.Id == integrationId);
		if (integration is null)
		{
			return;
		}

		try
		{
			await _widgetTypeProviders.StopAsync(integration, cancellationToken);
			await _layoutProviders.StopAsync(integration, cancellationToken);
			await _folderViewProviders.StopAsync(integration, cancellationToken);
			await _deviceProviders.StopAsync(integration, cancellationToken);
			await IntegrationShutdownRunner.RunAsync(integration, _timeProvider, cancellationToken);
			_logger.Information("Integration '{IntegrationId}' stopped before its configuration is rewritten",
				integrationId);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_logger.Information("Stopping integration '{IntegrationId}' was cancelled", integrationId);
		}
		catch (TimeoutException)
		{
			_logger.Error("Integration '{IntegrationId}' did not stop within {Timeout} and was abandoned",
				integrationId,
				IntegrationShutdownRunner.Timeout);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to stop integration '{IntegrationId}'", integrationId);
		}
	}

	public async Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default)
	{
		var integration = _registry.Integrations.FirstOrDefault(i => i.Id == integrationId);
		if (integration is null)
		{
			return;
		}

		// A notification is one finished sentence for the user, so the integration's name is resolved
		// once here rather than carried as a reference into text the host composes.
		var integrationName = await ActiveLocalization.Resolve(_scopeFactory, integration.Name);

		try
		{
			// The integration being reinitialized here can be the very one whose init just timed out, so
			// this shutdown-first step needs the same bounded, offloaded treatment as ShutdownAsync above -
			// otherwise resolving a hung-init issue by retrying it hangs the resolve request the same way.
			await _widgetTypeProviders.StopAsync(integration, cancellationToken);
			await _layoutProviders.StopAsync(integration, cancellationToken);
			await _folderViewProviders.StopAsync(integration, cancellationToken);
			await _deviceProviders.StopAsync(integration, cancellationToken);
			await IntegrationShutdownRunner.RunAsync(integration, _timeProvider, cancellationToken);

			if (_registry.GetOrigin(integrationId) == IntegrationOrigin.Plugin)
			{
				// RemotePluginIntegration.InitializeAsync is a deliberate no-op - the host does not own a
				// plugin process's lifecycle. Pushing host.state{config} is what actually closes issue
				// #413's config-change gap: the SDK's IntegrationLifecycleHostedService re-runs the
				// plugin's own InitializeAsync when it hears this, the same way it does on a reconnect.
				await PushConfigChangedAsync(integrationId, cancellationToken);
				_logger.Information("Integration '{IntegrationId}' reinitialized after config change", integrationId);
			}
			else
			{
				var outcome = await _initializer.InitializeAsync(integration, integrationName, cancellationToken);
				if (outcome == IntegrationInitializationOutcome.Initialized)
				{
					_logger.Information("Integration '{IntegrationId}' reinitialized after config change",
						integrationId);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to reinitialize integration '{IntegrationId}'", integrationId);
		}

		await _mediator.Publish(new IntegrationStateChangedNotification(integrationId), cancellationToken);
	}

	private Task<bool> PushConfigChangedAsync(string integrationId, CancellationToken cancellationToken)
		=> _pluginSessionRegistry.SendToPlugin(integrationId,
			new ProtocolEnvelope
			{
				Type = MessageTypes.HostState,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new HostStatePayload { Api = HostApis.Config },
					PluginProtocolJson.Options)
			},
			cancellationToken);
}
