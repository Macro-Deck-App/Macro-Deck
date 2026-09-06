using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateScriptRequestMessageHandler
	: IUiTransportMessageHandler<CreateScriptRequest, CreateScriptResponse>
{
	private readonly IScriptService _scriptService;

	public CreateScriptRequestMessageHandler(IScriptService scriptService)
	{
		_scriptService = scriptService;
	}

	public async ValueTask<CreateScriptResponse> Handle(
		CreateScriptRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _scriptService.Create(request.Name,
			request.Description,
			request.Flows,
			null,
			request.Inputs,
			request.RunsOnWidget);

		return result is { Success: true, Data: not null }
			? new CreateScriptResponse { Success = true, Script = ScriptDtoMapper.ToDto(result.Data) }
			: new CreateScriptResponse
			{
				Success = false,
				Error = ScriptDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
