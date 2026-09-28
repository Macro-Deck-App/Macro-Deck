using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RenameCatalogVariableRequestMessageHandler
	: IUiTransportMessageHandler<RenameCatalogVariableRequest, RenameCatalogVariableResponse>
{
	private readonly IVariableBindingService _bindings;
	private readonly VariableRegistry _registry;
	private readonly SharedVariables _sharedVariables;

	public RenameCatalogVariableRequestMessageHandler(IVariableBindingService bindings,
		VariableRegistry registry,
		SharedVariables sharedVariables)
	{
		_bindings = bindings;
		_registry = registry;
		_sharedVariables = sharedVariables;
	}

	public async ValueTask<RenameCatalogVariableResponse> Handle(
		RenameCatalogVariableRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.VariableId, out var id))
		{
			return new RenameCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(VariableBindingError.NotFound),
			};
		}

		var result = await _bindings.RenameAsync(id, request.Name, cancellationToken).ConfigureAwait(false);
		if (!result.Success)
		{
			return new RenameCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(result.Error!.Value, result.ErrorMessage),
			};
		}

		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return new RenameCatalogVariableResponse
			{
				Error = VariableBindingErrorMapper.ToTransportError(VariableBindingError.NotFound),
			};
		}

		var dynamicResourceId = _bindings.FindByVariableId(id)?.LocalResourceId;

		return new RenameCatalogVariableResponse
		{
			Variable = VariableDtoMapper.ToDto(entity, _registry.IsAvailable(id), dynamicResourceId,
				_sharedVariables.IsShared(entity)),
		};
	}
}
