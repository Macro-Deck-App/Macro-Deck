using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class BindCatalogVariableRequestMessageHandler
	: IUiTransportMessageHandler<BindCatalogVariableRequest, BindCatalogVariableResponse>
{
	private readonly IVariableBindingService _bindings;
	private readonly SharedVariables _sharedVariables;

	public BindCatalogVariableRequestMessageHandler(IVariableBindingService bindings,
		SharedVariables sharedVariables)
	{
		_bindings = bindings;
		_sharedVariables = sharedVariables;
	}

	public async ValueTask<BindCatalogVariableResponse> Handle(
		BindCatalogVariableRequest request,
		CancellationToken cancellationToken)
	{
		var typeOverride = VariableDtoMapper.TypeFromWire(request.Type);

		var result = await _bindings.BindAsync(request.IntegrationId,
				request.ResourceId,
				request.Name,
				typeOverride,
				cancellationToken)
			.ConfigureAwait(false);

		if (!result.Success || result.Data is null)
		{
			return new BindCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(result.Error!.Value, result.ErrorMessage),
			};
		}

		var dynamicResourceId = _bindings.FindByVariableId(result.Data.Id)?.LocalResourceId;

		return new BindCatalogVariableResponse
		{
			Variable = VariableDtoMapper.ToDto(result.Data, true, dynamicResourceId,
				_sharedVariables.IsShared(result.Data)),
		};
	}
}
