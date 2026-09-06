using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateAutomationRequestMessageHandler
	: IUiTransportMessageHandler<CreateAutomationRequest, CreateAutomationResponse>
{
	private readonly IAutomationService _automationService;

	public CreateAutomationRequestMessageHandler(IAutomationService automationService)
	{
		_automationService = automationService;
	}

	public async ValueTask<CreateAutomationResponse> Handle(
		CreateAutomationRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _automationService.Create(request.Name, request.Description, request.Flows);

		return result is { Success: true, Data: not null }
			? new CreateAutomationResponse { Success = true, Automation = AutomationDtoMapper.ToDto(result.Data) }
			: new CreateAutomationResponse
			{
				Success = false,
				Error = AutomationDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
