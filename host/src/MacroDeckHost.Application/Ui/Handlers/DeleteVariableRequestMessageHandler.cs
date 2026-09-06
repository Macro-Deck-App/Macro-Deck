using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteVariableRequestMessageHandler
	: IUiTransportMessageHandler<DeleteVariableRequest, DeleteVariableResponse>
{
	private readonly IVariableService _service;

	public DeleteVariableRequestMessageHandler(IVariableService service)
	{
		_service = service;
	}

	public async ValueTask<DeleteVariableResponse> Handle(
		DeleteVariableRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DeleteVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "ValidationError", Message = AppStrings.Errors.Variables.InvalidId() }
			};
		}

		var result = await _service.DeleteUserVariable(id);
		if (!result.Success)
		{
			return new DeleteVariableResponse
			{
				Success = false,
				Error = VariableDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
		}

		return new DeleteVariableResponse { Success = true };
	}
}
