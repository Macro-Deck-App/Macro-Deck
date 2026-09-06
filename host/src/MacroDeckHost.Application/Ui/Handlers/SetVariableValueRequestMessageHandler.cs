using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetVariableValueRequestMessageHandler
	: IUiTransportMessageHandler<SetVariableValueRequest, SetVariableValueResponse>
{
	private readonly IVariableService _service;
	private readonly IHostLockState _lockState;

	public SetVariableValueRequestMessageHandler(IVariableService service, IHostLockState lockState)
	{
		_service = service;
		_lockState = lockState;
	}

	public async ValueTask<SetVariableValueResponse> Handle(
		SetVariableValueRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new SetVariableValueResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "ValidationError", Message = AppStrings.Errors.Variables.InvalidId() }
			};
		}

		// A variable write fires variable.changed triggers and onStateChange flows, and now also reaches
		// the owning application for a provider-owned variable, so this must be gated too - not just the
		// direct action-execution paths.
		if (_lockState.IsLocked)
		{
			return new SetVariableValueResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			};
		}

		var entity = await _service.GetById(id);
		if (entity is null)
		{
			return new SetVariableValueResponse
			{
				Success = false,
				Error = new TransportError { Code = "NotFound", Message = AppStrings.Errors.Variables.NotFound() }
			};
		}

		var parsedValue = VariableDtoMapper.ParseInputValue(entity.Type, request.Value);
		var result = await _service.SetValue(id, parsedValue, cancellationToken);

		if (!result.Success || result.Data is null)
		{
			return new SetVariableValueResponse
			{
				Success = false,
				Error = VariableDtoMapper.ToWriteError(result.Error!.Value,
					result.ErrorMessage,
					entity.OwnerIntegrationId)
			};
		}

		return new SetVariableValueResponse
		{
			Success = true,
			Pending = entity.Classification == VariableClassification.Integration,
			Variable = VariableDtoMapper.ToDto(result.Data, true, null)
		};
	}
}
