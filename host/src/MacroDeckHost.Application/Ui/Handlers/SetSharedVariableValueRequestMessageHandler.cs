using MacroDeck.Localization;
using System.Globalization;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetSharedVariableValueRequestMessageHandler
	: IUiTransportMessageHandler<SetSharedVariableValueRequest, SetSharedVariableValueResponse>
{
	private readonly SharedVariables _sharedVariables;
	private readonly IVariableService _service;
	private readonly IHostLockState _lockState;

	public SetSharedVariableValueRequestMessageHandler(SharedVariables sharedVariables,
		IVariableService service,
		IHostLockState lockState)
	{
		_sharedVariables = sharedVariables;
		_service = service;
		_lockState = lockState;
	}

	public async ValueTask<SetSharedVariableValueResponse> Handle(
		SetSharedVariableValueRequest request,
		CancellationToken cancellationToken)
	{
		if (_lockState.IsLocked)
		{
			return Failed(ActionExecutionErrorCodes.HostLocked, AppStrings.Errors.Common.HostLocked());
		}

		var variable = _sharedVariables.FindShared(request.Name);
		if (variable is null)
		{
			return Failed("NotFound", AppStrings.Errors.Variables.NotShared());
		}

		if (!TryParse(variable.Type, request.Value, out var value))
		{
			return Failed(nameof(VariableError.InvalidValue), AppStrings.Errors.Variables.ValueDoesNotFitType());
		}

		var result = await _service.SetValue(variable.Id, value, cancellationToken);
		if (!result.Success)
		{
			return new SetSharedVariableValueResponse
			{
				Success = false,
				Error = VariableDtoMapper.ToWriteError(result.Error!.Value, result.ErrorMessage, variable.OwnerIntegrationId)
			};
		}

		return new SetSharedVariableValueResponse { Success = true };
	}

	private static bool TryParse(VariableType type, string? raw, out object? value)
	{
		switch (type)
		{
			case VariableType.Numeric:
				var parsed = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number);
				value = number;
				return parsed;
			case VariableType.Boolean:
				var isBool = bool.TryParse(raw, out var flag);
				value = flag;
				return isBool;
			default:
				value = raw ?? string.Empty;
				return true;
		}
	}

	private static SetSharedVariableValueResponse Failed(string code, LocalizedText message)
		=> new() { Success = false, Error = new TransportError { Code = code, Message = message } };
}
