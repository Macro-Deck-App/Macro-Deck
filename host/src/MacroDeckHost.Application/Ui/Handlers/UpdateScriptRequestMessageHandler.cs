using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateScriptRequestMessageHandler
	: IUiTransportMessageHandler<UpdateScriptRequest, UpdateScriptResponse>
{
	private readonly IScriptService _scriptService;

	public UpdateScriptRequestMessageHandler(IScriptService scriptService)
	{
		_scriptService = scriptService;
	}

	public async ValueTask<UpdateScriptResponse> Handle(
		UpdateScriptRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new UpdateScriptResponse { Success = false, Error = ScriptDtoMapper.InvalidId() };
		}

		var result = await _scriptService.Update(id,
			request.Name,
			request.Description,
			request.Flows,
			request.Inputs,
			request.RunsOnWidget);

		return result is { Success: true, Data: not null }
			? new UpdateScriptResponse { Success = true, Script = ScriptDtoMapper.ToDto(result.Data) }
			: new UpdateScriptResponse
			{
				Success = false,
				Error = ScriptDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
