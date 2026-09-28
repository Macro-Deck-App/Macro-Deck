using MacroDeck.Localization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Mediator;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetVariableSharedRequestMessageHandler
	: IUiTransportMessageHandler<SetVariableSharedRequest, SetVariableSharedResponse>
{
	private readonly SharedVariables _sharedVariables;
	private readonly VariableRegistry _registry;
	private readonly IMediator _mediator;

	public SetVariableSharedRequestMessageHandler(SharedVariables sharedVariables,
		VariableRegistry registry,
		IMediator mediator)
	{
		_sharedVariables = sharedVariables;
		_registry = registry;
		_mediator = mediator;
	}

	public async ValueTask<SetVariableSharedResponse> Handle(
		SetVariableSharedRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return Failed("ValidationError", AppStrings.Errors.Variables.InvalidId());
		}

		var result = _sharedVariables.SetShared(id, request.Shared);
		if (!result.Success || result.Data is null)
		{
			return result.Error switch
			{
				VariableError.NotFound => Failed("NotFound", AppStrings.Errors.Variables.NotFound()),
				VariableError.ValidationError => Failed("ValidationError",
					AppStrings.Errors.Variables.OnlyGlobalCanBeShared()),
				VariableError.NotEditable => Failed("NotEditable", AppStrings.Errors.Variables.ImportedCannotBeShared()),
				_ => Failed("InternalError", AppStrings.Errors.Variables.SharingNotSaved())
			};
		}

		await _mediator.Publish(new VariableUpdatedNotification(result.Data), cancellationToken);

		return new SetVariableSharedResponse
		{
			Success = true,
			Variable = VariableDtoMapper.ToDto(result.Data,
				_registry.IsAvailable(result.Data.Id),
				null,
				_sharedVariables.IsShared(result.Data))
		};
	}

	private static SetVariableSharedResponse Failed(string code, LocalizedText message)
		=> new() { Success = false, Error = new TransportError { Code = code, Message = message } };
}
