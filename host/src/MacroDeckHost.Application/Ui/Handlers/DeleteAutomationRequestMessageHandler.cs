using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteAutomationRequestMessageHandler
	: IUiTransportMessageHandler<DeleteAutomationRequest, DeleteAutomationResponse>
{
	private readonly IAutomationService _automationService;

	public DeleteAutomationRequestMessageHandler(IAutomationService automationService)
	{
		_automationService = automationService;
	}

	public async ValueTask<DeleteAutomationResponse> Handle(
		DeleteAutomationRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DeleteAutomationResponse { Success = false, Error = AutomationDtoMapper.InvalidId() };
		}

		var result = await _automationService.Delete(id);

		return result.Success
			? new DeleteAutomationResponse { Success = true }
			: new DeleteAutomationResponse
			{
				Success = false,
				Error = AutomationDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
