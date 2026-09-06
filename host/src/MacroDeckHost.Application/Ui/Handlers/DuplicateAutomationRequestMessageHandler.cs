using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DuplicateAutomationRequestMessageHandler
	: IUiTransportMessageHandler<DuplicateAutomationRequest, DuplicateAutomationResponse>
{
	private readonly IAutomationService _automationService;

	public DuplicateAutomationRequestMessageHandler(IAutomationService automationService)
	{
		_automationService = automationService;
	}

	public async ValueTask<DuplicateAutomationResponse> Handle(
		DuplicateAutomationRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DuplicateAutomationResponse { Success = false, Error = AutomationDtoMapper.InvalidId() };
		}

		var result = await _automationService.Duplicate(id);

		return result is { Success: true, Data: not null }
			? new DuplicateAutomationResponse { Success = true, Automation = AutomationDtoMapper.ToDto(result.Data) }
			: new DuplicateAutomationResponse
			{
				Success = false,
				Error = AutomationDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
