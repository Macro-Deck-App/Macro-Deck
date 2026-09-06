using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteScriptRequestMessageHandler
	: IUiTransportMessageHandler<DeleteScriptRequest, DeleteScriptResponse>
{
	private readonly IScriptService _scriptService;

	public DeleteScriptRequestMessageHandler(IScriptService scriptService)
	{
		_scriptService = scriptService;
	}

	public async ValueTask<DeleteScriptResponse> Handle(
		DeleteScriptRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DeleteScriptResponse { Success = false, Error = ScriptDtoMapper.InvalidId() };
		}

		var result = await _scriptService.Delete(id);

		return result.Success
			? new DeleteScriptResponse { Success = true }
			: new DeleteScriptResponse
			{
				Success = false,
				Error = ScriptDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
