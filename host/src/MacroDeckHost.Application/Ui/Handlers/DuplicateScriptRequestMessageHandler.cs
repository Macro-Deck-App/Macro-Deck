using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DuplicateScriptRequestMessageHandler
	: IUiTransportMessageHandler<DuplicateScriptRequest, DuplicateScriptResponse>
{
	private readonly IScriptService _scriptService;

	public DuplicateScriptRequestMessageHandler(IScriptService scriptService)
	{
		_scriptService = scriptService;
	}

	public async ValueTask<DuplicateScriptResponse> Handle(
		DuplicateScriptRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DuplicateScriptResponse { Success = false, Error = ScriptDtoMapper.InvalidId() };
		}

		var result = await _scriptService.Duplicate(id);

		return result is { Success: true, Data: not null }
			? new DuplicateScriptResponse { Success = true, Script = ScriptDtoMapper.ToDto(result.Data) }
			: new DuplicateScriptResponse
			{
				Success = false,
				Error = ScriptDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
	}
}
