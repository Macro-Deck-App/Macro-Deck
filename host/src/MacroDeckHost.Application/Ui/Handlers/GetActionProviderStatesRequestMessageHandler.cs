using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;
using Serilog;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

/// <summary>
/// Lets the editor ask a configured state-provider action instance what states it currently declares,
/// before the button has adopted it - modelled on <see cref="GetActionParameterOptionsRequestMessageHandler" />.
/// </summary>
public class GetActionProviderStatesRequestMessageHandler
	: IUiTransportMessageHandler<GetActionProviderStatesRequest, GetActionProviderStatesResponse>
{
	private static readonly TimeSpan _providerTimeout = TimeSpan.FromSeconds(10);

	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly ILogger _logger;

	public GetActionProviderStatesRequestMessageHandler(IIntegrationRegistry integrationRegistry, ILogger logger)
	{
		_integrationRegistry = integrationRegistry;
		_logger = logger.ForContext<GetActionProviderStatesRequestMessageHandler>();
	}

	private static ActionProviderStateDto ToDto(ActionStateDefinition state)
		=> new()
		{
			Id = state.Id,
			Label = state.Label,
			DefaultAppearance = state.DefaultAppearance is { } appearance
				? new ActionProviderStateAppearanceDto
				{
					Label = appearance.Label,
					BackgroundColor = appearance.BackgroundColor,
					LabelColor = appearance.LabelColor,
					IconId = appearance.IconId
				}
				: null
		};

	public async ValueTask<GetActionProviderStatesResponse> Handle(
		GetActionProviderStatesRequest request,
		CancellationToken cancellationToken)
	{
		if (!_integrationRegistry.IsEnabled(request.IntegrationId) ||
			_integrationRegistry.FindAction(request.IntegrationId, request.ActionId) is not
				IStateProviderActionDefinition provider)
		{
			return new GetActionProviderStatesResponse
			{
				Error = new TransportError
				{
					Code = "ACTION_NOT_FOUND",
					Message = AppStrings.Errors.Actions.ProviderNotFound(integrationId: request.IntegrationId,
						actionId: request.ActionId)
				}
			};
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_providerTimeout);

		try
		{
			var parameters = ActionParameterConverter.ToNullable(request.Parameters ?? []);
			var snapshot = await provider.GetActionStateAsync(parameters, timeoutCts.Token);

			return new GetActionProviderStatesResponse
			{
				States = snapshot?.States.Select(ToDto).ToList() ?? []
			};
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return new GetActionProviderStatesResponse
			{
				Error = new TransportError
					{ Code = "OPTIONS_TIMEOUT", Message = AppStrings.Errors.Actions.ProviderTimeout() }
			};
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Failed to read provider states for {Integration}.{Action}",
				request.IntegrationId,
				request.ActionId);
			return new GetActionProviderStatesResponse
			{
				Error = new TransportError { Code = "OPTIONS_FAILED", Message = ex.Message }
			};
		}
	}
}
