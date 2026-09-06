using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetScriptsRequestMessageHandler : IUiTransportMessageHandler<GetScriptsRequest, GetScriptsResponse>
{
	private readonly IScriptService _scriptService;
	private readonly StartupReadiness _readiness;

	public GetScriptsRequestMessageHandler(IScriptService scriptService, StartupReadiness readiness)
	{
		_scriptService = scriptService;
		_readiness = readiness;
	}

	public async ValueTask<GetScriptsResponse> Handle(GetScriptsRequest request, CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not observe the still-empty
		// script cache as "no scripts".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		return new GetScriptsResponse
		{
			Scripts = _scriptService.GetAll().Select(ScriptDtoMapper.ToDto).ToList()
		};
	}
}
