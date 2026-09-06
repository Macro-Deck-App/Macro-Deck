using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateVariableRequestMessageHandler
	: IUiTransportMessageHandler<CreateVariableRequest, CreateVariableResponse>
{
	private readonly IVariableService _service;

	public CreateVariableRequestMessageHandler(IVariableService service)
	{
		_service = service;
	}

	public async ValueTask<CreateVariableResponse> Handle(
		CreateVariableRequest request,
		CancellationToken cancellationToken)
	{
		var scope = VariableDtoMapper.ScopeFromWire(request.Scope);
		var type = VariableDtoMapper.TypeFromWire(request.Type);

		if (scope is null || type is null)
		{
			return new CreateVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "ValidationError", Message = AppStrings.Errors.Variables.InvalidScopeOrType() }
			};
		}

		var initialValue = VariableDtoMapper.ParseInputValue(type.Value, request.InitialValue);

		var result = await _service.CreateUserVariable(request.Name,
			scope.Value,
			request.ScopeRefId,
			type.Value,
			initialValue,
			request.DecimalPlaces);

		if (!result.Success || result.Data is null)
		{
			return new CreateVariableResponse
			{
				Success = false,
				Error = VariableDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
		}

		return new CreateVariableResponse
		{
			Success = true,
			Variable = VariableDtoMapper.ToDto(result.Data, true, null)
		};
	}
}
