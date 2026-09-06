using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RunScriptRequestMessageHandler : IUiTransportMessageHandler<RunScriptRequest, RunScriptResponse>
{
	private readonly IScriptService _scriptService;
	private readonly IScriptRunner _scriptRunner;
	private readonly IHostLockState _lockState;

	public RunScriptRequestMessageHandler(
		IScriptService scriptService,
		IScriptRunner scriptRunner,
		IHostLockState lockState)
	{
		_scriptService = scriptService;
		_scriptRunner = scriptRunner;
		_lockState = lockState;
	}

	public async ValueTask<RunScriptResponse> Handle(RunScriptRequest request, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new RunScriptResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = ScriptDtoMapper.InvalidId()
			};
		}

		if (_lockState.IsLocked)
		{
			return new RunScriptResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			};
		}

		if (_scriptService.GetById(id) is null)
		{
			return new RunScriptResponse
			{
				Success = false,
				Status = ActionExecutionStatus.Failed,
				Error = ScriptDtoMapper.ToError(ScriptError.NotFound, AppStrings.Errors.Scripts.NotFound())
			};
		}

		var result = await _scriptRunner.RunAsync(id,
			request.ClientId,
			cancellationToken,
			request.CallDepth ?? 0,
			request.Inputs,
			request.OwnerWidgetId);
		var dto = ActionExecutionDtoMapper.ToDto(result);

		return new RunScriptResponse
		{
			Success = ActionExecutionDtoMapper.IsSuccess(dto.Status),
			Error = dto.Error,
			ExecutionId = dto.ExecutionId,
			Status = dto.Status,
			DurationMs = dto.DurationMs,
			Actions = dto.Actions,
			AppliedInputs = [.. result.AppliedInputs]
		};
	}
}
