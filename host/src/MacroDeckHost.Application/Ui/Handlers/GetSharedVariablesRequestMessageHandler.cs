using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetSharedVariablesRequestMessageHandler
	: IUiTransportMessageHandler<GetSharedVariablesRequest, GetSharedVariablesResponse>
{
	private readonly SharedVariables _sharedVariables;
	private readonly VariableRegistry _registry;
	private readonly StartupReadiness _readiness;

	public GetSharedVariablesRequestMessageHandler(SharedVariables sharedVariables,
		VariableRegistry registry,
		StartupReadiness readiness)
	{
		_sharedVariables = sharedVariables;
		_registry = registry;
		_readiness = readiness;
	}

	public async ValueTask<GetSharedVariablesResponse> Handle(
		GetSharedVariablesRequest request,
		CancellationToken cancellationToken)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var response = new GetSharedVariablesResponse();
		foreach (var entry in _sharedVariables.List())
		{
			var variable = entry.Variable;
			response.Variables.Add(new SharedVariableDto
			{
				Name = entry.Name,
				Type = VariableDtoMapper.TypeToWire(entry.Type),
				Value = variable?.Value ?? string.Empty,
				Present = variable is not null,
				Available = variable is not null && _registry.IsAvailable(variable.Id),
				CanWrite = variable?.CanWrite ?? false,
				CommitOnRelease = variable?.CommitOnRelease ?? false,
				DecimalPlaces = variable?.DecimalPlaces,
				Unit = variable?.Unit,
				Min = variable?.Min,
				Max = variable?.Max,
				Step = variable?.Step
			});
		}

		return response;
	}
}
