using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Auth;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/companion/scripts")]
public sealed class CompanionScriptsController : ControllerBase
{
	public static readonly TimeSpan RunBound = TimeSpan.FromSeconds(10);

	private readonly IScriptService _scripts;
	private readonly IHostLockState _lockState;
	private readonly StartupReadiness _readiness;
	private readonly IActionExecutionCoordinator _coordinator;

	public CompanionScriptsController(
		IScriptService scripts,
		IHostLockState lockState,
		StartupReadiness readiness,
		IActionExecutionCoordinator coordinator)
	{
		_scripts = scripts;
		_lockState = lockState;
		_readiness = readiness;
		_coordinator = coordinator;
	}

	[HttpGet]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<GetCompanionScriptsResponse> GetAll(CancellationToken ct)
	{
		await _readiness.WhenReady.WaitAsync(ct);

		return new GetCompanionScriptsResponse
		{
			Scripts = _scripts.GetAll()
				.Where(script => !script.RunsOnWidget)
				.Select(script => new CompanionScript
				{
					Id = script.Id.ToString(),
					Name = script.Name,
					Description = script.Description,
					Inputs = [.. script.Inputs]
				})
				.ToList()
		};
	}

	[HttpPost("{id}/run")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<RunCompanionScriptResponse> Run(string id, RunCompanionScriptRequest body, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var scriptId))
		{
			return Failed(ScriptDtoMapper.InvalidId());
		}

		await _readiness.WhenReady.WaitAsync(ct);

		if (_lockState.IsLocked)
		{
			return Failed(new TransportError
				{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() });
		}

		if (_scripts.GetById(scriptId) is not { RunsOnWidget: false })
		{
			return Failed(ScriptDtoMapper.ToError(ScriptError.NotFound, AppStrings.Errors.Scripts.NotFound()));
		}

		var inputs = body.Inputs;
		var clientId = body.ClientId;
		var dispatch = await _coordinator.RunBoundedAsync(
			(services, token) => services.GetRequiredService<IScriptRunner>().RunAsync(scriptId, clientId, token, inputs: inputs),
			RunBound,
			ct);

		if (dispatch.Result is not { } result)
		{
			return new RunCompanionScriptResponse { Success = true, Status = ActionExecutionStatus.Accepted };
		}

		var dto = ActionExecutionDtoMapper.ToDto(result);
		return new RunCompanionScriptResponse
		{
			Success = ActionExecutionDtoMapper.IsSuccess(dto.Status),
			Status = dto.Status,
			Error = dto.Error
		};
	}

	private static RunCompanionScriptResponse Failed(TransportError error)
		=> new() { Success = false, Status = ActionExecutionStatus.Failed, Error = error };
}
