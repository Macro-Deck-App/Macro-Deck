using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateAutomationRequestMessageHandler
	: IUiTransportMessageHandler<UpdateAutomationRequest, UpdateAutomationResponse>
{
	private readonly IAutomationService _automationService;

	public UpdateAutomationRequestMessageHandler(IAutomationService automationService)
	{
		_automationService = automationService;
	}

	public async ValueTask<UpdateAutomationResponse> Handle(
		UpdateAutomationRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new UpdateAutomationResponse { Success = false, Error = AutomationDtoMapper.InvalidId() };
		}

		var result = await _automationService.Update(id,
			request.Name,
			request.Description,
			request.Flows,
			request.Enabled);

		return result is { Success: true, Data: not null }
			? new UpdateAutomationResponse { Success = true, Automation = AutomationDtoMapper.ToDto(result.Data) }
			: new UpdateAutomationResponse
			{
				Success = false,
				Error = AutomationDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
