using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

/// <summary>
/// Lets the editor ask a configured icon-provider action instance what icon it currently reports, before
/// the button has adopted it (issue #425) - modelled directly on
/// <see cref="GetActionProviderStatesRequestMessageHandler" />, including its 10 s timeout: that is a
/// deliberately different budget from <c>WidgetIconService</c>'s 3 s render-path timeout, since a person
/// watching the editor can tolerate a slower answer than a widget mid-render.
/// </summary>
public class GetActionProviderIconRequestMessageHandler
	: IUiTransportMessageHandler<GetActionProviderIconRequest, GetActionProviderIconResponse>
{
	private static readonly TimeSpan _providerTimeout = TimeSpan.FromSeconds(10);

	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly RemoteIconProviderActionRegistry _remoteIconProviders;
	private readonly ILogger _logger;

	public GetActionProviderIconRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		RemoteIconProviderActionRegistry remoteIconProviders,
		ILogger logger)
	{
		_integrationRegistry = integrationRegistry;
		_remoteIconProviders = remoteIconProviders;
		_logger = logger.ForContext<GetActionProviderIconRequestMessageHandler>();
	}

	public async ValueTask<GetActionProviderIconResponse> Handle(
		GetActionProviderIconRequest request,
		CancellationToken cancellationToken)
	{
		var provider = ResolveProvider(request.IntegrationId, request.ActionId);
		if (provider is null)
		{
			return new GetActionProviderIconResponse
			{
				Error = new TransportError
				{
					Code = "ACTION_NOT_FOUND",
					Message = AppStrings.Errors.Actions.ActionNotFound(integrationId: request.IntegrationId,
						actionId: request.ActionId)
				}
			};
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_providerTimeout);

		try
		{
			var parameters = ActionParameterConverter.ToNullable(request.Parameters ?? []);
			var snapshot = await provider.GetActionIconAsync(parameters, timeoutCts.Token);

			// A null snapshot ("cannot answer right now") is a well-formed, non-error answer - never
			// collapsed with a transport failure (issue #425 decision 9).
			if (snapshot is null)
			{
				return new GetActionProviderIconResponse { HasSnapshot = false };
			}

			return new GetActionProviderIconResponse
			{
				HasSnapshot = true,
				Version = snapshot.Version,
				Reference = snapshot.Reference is { } reference
					? new ActionProviderIconReferenceDto { Type = reference.Type, Reference = reference.Reference }
					: null,
				MediaType = snapshot.MediaType,
				NoIcon = snapshot.NoIcon
			};
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new GetActionProviderIconResponse
			{
				Error = new TransportError
					{ Code = "OPTIONS_TIMEOUT", Message = AppStrings.Errors.Actions.ProviderTimeout() }
			};
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Failed to probe icon-provider action for {Integration}.{Action}",
				request.IntegrationId,
				request.ActionId);
			return new GetActionProviderIconResponse
			{
				Error = new TransportError { Code = "OPTIONS_FAILED", Message = ex.Message }
			};
		}
	}

	private IIconProviderActionDefinition? ResolveProvider(string integrationId, string actionId)
	{
		if (!_integrationRegistry.IsEnabled(integrationId))
		{
			return null;
		}

		var action = _integrationRegistry.FindAction(integrationId, actionId);

		return action switch
		{
			IIconProviderActionDefinition direct => direct,
			RemoteActionDefinition { ProvidesIcon: true } => _remoteIconProviders.Resolve(integrationId, actionId),
			_ => null
		};
	}
}
