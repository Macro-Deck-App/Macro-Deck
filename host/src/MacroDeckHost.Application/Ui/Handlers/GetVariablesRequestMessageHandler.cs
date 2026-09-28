using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetVariablesRequestMessageHandler
	: IUiTransportMessageHandler<GetVariablesRequest, GetVariablesResponse>
{
	private readonly IVariableService _service;
	private readonly VariableRegistry _registry;
	private readonly IVariableBindingService _dynamicBindings;
	private readonly StartupReadiness _readiness;
	private readonly SharedVariables _sharedVariables;

	public GetVariablesRequestMessageHandler(IVariableService service,
		VariableRegistry registry,
		IVariableBindingService dynamicBindings,
		StartupReadiness readiness,
		SharedVariables sharedVariables)
	{
		_service = service;
		_registry = registry;
		_dynamicBindings = dynamicBindings;
		_readiness = readiness;
		_sharedVariables = sharedVariables;
	}

	public async ValueTask<GetVariablesResponse> Handle(
		GetVariablesRequest request,
		CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not
		// observe the still-loading variable registry as "no variables".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var scope = VariableDtoMapper.ScopeFromWire(request.Scope);

		var entities = scope.HasValue
			? await _service.GetByScope(scope.Value, request.ScopeRefId)
			: await _service.GetAll();

		var response = new GetVariablesResponse();
		foreach (var entity in entities)
		{
			var dynamicResourceId = _dynamicBindings.FindByVariableId(entity.Id)?.LocalResourceId;
			response.Variables.Add(VariableDtoMapper.ToDto(entity,
				_registry.IsAvailable(entity.Id),
				dynamicResourceId,
				_sharedVariables.IsShared(entity)));
		}

		return response;
	}
}
