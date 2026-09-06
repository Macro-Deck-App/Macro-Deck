using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class StartConfigFlowRequestMessageHandler
	: IUiTransportMessageHandler<StartConfigFlowRequest, StartConfigFlowResponse>
{
	private readonly IConfigFlowManager _manager;

	public StartConfigFlowRequestMessageHandler(IConfigFlowManager manager)
	{
		_manager = manager;
	}

	public async ValueTask<StartConfigFlowResponse> Handle(
		StartConfigFlowRequest request,
		CancellationToken cancellationToken)
	{
		if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
		{
			return new StartConfigFlowResponse
			{
				Supported = true,
				Error = new TransportError
				{
					Code = "TITLE_REQUIRED",
					Message = AppStrings.Errors.Config.TitleRequired()
				}
			};
		}

		Guid? entryId = null;
		if (request.EntryId is { } rawEntryId)
		{
			if (!Guid.TryParse(rawEntryId, out var parsedEntryId))
			{
				return new StartConfigFlowResponse
				{
					Supported = true,
					Error = new TransportError
					{
						Code = "INVALID_ID",
						Message = AppStrings.Errors.Config.InvalidEntryId(id: rawEntryId)
					}
				};
			}

			entryId = parsedEntryId;
		}

		var outcome = await _manager.StartAsync(request.IntegrationId,
			request.Title,
			entryId,
			cancellationToken);

		if (!outcome.Supported)
		{
			return new StartConfigFlowResponse
			{
				Supported = false,
				Error = new TransportError
				{
					Code = "NOT_SUPPORTED",
					Message = AppStrings.Errors.Integrations.NoConfigFlow(id: request.IntegrationId)
				}
			};
		}

		if (outcome.ErrorMessage is { } errorMessage)
		{
			return new StartConfigFlowResponse
			{
				Supported = true,
				Error = new TransportError { Code = "OAUTH_UNAVAILABLE", Message = errorMessage }
			};
		}

		return new StartConfigFlowResponse
		{
			Supported = true,
			FlowId = outcome.FlowId.ToString(),
			Step = outcome.Step is null ? null : ConfigFlowStepMapper.Map(outcome.Step),
			SupportsConfigUi = outcome.SupportsConfigUi,
			ConfigUiModelVersion = outcome.ConfigUiModelVersion,
			InitialValues = outcome.InitialValues?.ToDictionary() ??
				new Dictionary<string, global::System.Text.Json.JsonElement>(),
			StoredSecretFields = outcome.StoredSecretFields?.ToList() ?? []
		};
	}
}
