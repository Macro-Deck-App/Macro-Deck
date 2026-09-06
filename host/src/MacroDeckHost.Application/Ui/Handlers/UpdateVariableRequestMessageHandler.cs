using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateVariableRequestMessageHandler
	: IUiTransportMessageHandler<UpdateVariableRequest, UpdateVariableResponse>
{
	private readonly IVariableService _service;
	private readonly IHostLockState _lockState;

	public UpdateVariableRequestMessageHandler(IVariableService service, IHostLockState lockState)
	{
		_service = service;
		_lockState = lockState;
	}

	public async ValueTask<UpdateVariableResponse> Handle(
		UpdateVariableRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new UpdateVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "ValidationError", Message = AppStrings.Errors.Variables.InvalidId() }
			};
		}

		// The value leg reaches the owning application for a provider-owned variable, so this path needs
		// the same lock gate the dedicated value handler has always had.
		if (_lockState.IsLocked)
		{
			return new UpdateVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			};
		}

		var entity = await _service.GetById(id);
		if (entity is null)
		{
			return new UpdateVariableResponse
			{
				Success = false,
				Error = new TransportError { Code = "NotFound", Message = AppStrings.Errors.Variables.NotFound() }
			};
		}

		var current = entity;

		// Definition first: a rename the user asked for in the same request must survive a value write that
		// is only applied by the owner and reported back later.
		if (request.Name is not null || request.DecimalPlaces is not null)
		{
			var definition = await _service.UpdateUserVariable(id, request.Name, request.DecimalPlaces);
			if (!definition.Success || definition.Data is null)
			{
				return Failed(definition.Error!.Value, definition.ErrorMessage, entity.OwnerIntegrationId);
			}

			current = definition.Data;
		}

		if (request.Value is not null)
		{
			var parsedValue = VariableDtoMapper.ParseInputValue(entity.Type, request.Value);
			var value = await _service.SetValue(id, parsedValue, cancellationToken);
			if (!value.Success || value.Data is null)
			{
				return Failed(value.Error!.Value, value.ErrorMessage, entity.OwnerIntegrationId);
			}

			current = value.Data;
		}

		return new UpdateVariableResponse
		{
			Success = true,
			Variable = VariableDtoMapper.ToDto(current, true, null)
		};
	}

	private static UpdateVariableResponse Failed(
		VariableError error,
		string? message,
		string? ownerIntegrationId)
		=> new()
		{
			Success = false,
			Error = VariableDtoMapper.ToWriteError(error, message, ownerIntegrationId)
		};
}
