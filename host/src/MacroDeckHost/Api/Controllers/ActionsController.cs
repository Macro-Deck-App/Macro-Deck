using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/actions")]
public class ActionsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetActionsRequest, GetActionsResponse> _getActions;

	private readonly IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest, ExecuteActionButtonTriggerResponse>
		_executeActionButtonTrigger;

	private readonly IUiTransportMessageHandler<ExecuteActionRequest, ExecuteActionResponse> _executeAction;

	private readonly IUiTransportMessageHandler<RunActionFlowRequest, RunActionFlowResponse> _runActionFlow;

	private readonly IUiTransportMessageHandler<GetActionParameterOptionsRequest, GetActionParameterOptionsResponse>
		_getParameterOptions;

	private readonly IUiTransportMessageHandler<GetActionProviderStatesRequest, GetActionProviderStatesResponse>
		_getProviderStates;

	private readonly IUiTransportMessageHandler<GetActionProviderIconRequest, GetActionProviderIconResponse>
		_getProviderIcon;

	public ActionsController(
		IUiTransportMessageHandler<GetActionsRequest, GetActionsResponse> getActions,
		IUiTransportMessageHandler<ExecuteActionButtonTriggerRequest, ExecuteActionButtonTriggerResponse>
			executeActionButtonTrigger,
		IUiTransportMessageHandler<ExecuteActionRequest, ExecuteActionResponse> executeAction,
		IUiTransportMessageHandler<RunActionFlowRequest, RunActionFlowResponse> runActionFlow,
		IUiTransportMessageHandler<GetActionParameterOptionsRequest, GetActionParameterOptionsResponse>
			getParameterOptions,
		IUiTransportMessageHandler<GetActionProviderStatesRequest, GetActionProviderStatesResponse>
			getProviderStates,
		IUiTransportMessageHandler<GetActionProviderIconRequest, GetActionProviderIconResponse> getProviderIcon)
	{
		_getActions = getActions;
		_executeActionButtonTrigger = executeActionButtonTrigger;
		_executeAction = executeAction;
		_runActionFlow = runActionFlow;
		_getParameterOptions = getParameterOptions;
		_getProviderStates = getProviderStates;
		_getProviderIcon = getProviderIcon;
	}

	[HttpGet]
	public Task<GetActionsResponse> GetAll(CancellationToken ct)
		=> _getActions.Handle(new GetActionsRequest(), ct).AsTask();

	[HttpPost("execute")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<ExecuteActionButtonTriggerResponse> Execute(
		ExecuteActionButtonTriggerRequest body,
		CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(body);

		return _executeActionButtonTrigger.Handle(body, ct).AsTask();
	}

	[HttpPost("run")]
	public Task<ExecuteActionResponse> Run(
		ExecuteActionRequest body,
		CancellationToken ct) => _executeAction.Handle(body, ct).AsTask();

	[HttpPost("run-flow")]
	public Task<RunActionFlowResponse> RunFlow(
		RunActionFlowRequest body,
		CancellationToken ct) => _runActionFlow.Handle(body, ct).AsTask();

	[HttpPost("options")]
	public Task<GetActionParameterOptionsResponse> GetParameterOptions(
		GetActionParameterOptionsRequest body,
		CancellationToken ct) => _getParameterOptions.Handle(body, ct).AsTask();

	[HttpPost("provider-states")]
	public Task<GetActionProviderStatesResponse> GetProviderStates(
		GetActionProviderStatesRequest body,
		CancellationToken ct) => _getProviderStates.Handle(body, ct).AsTask();

	[HttpPost("provider-icon")]
	public Task<GetActionProviderIconResponse> GetProviderIcon(
		GetActionProviderIconRequest body,
		CancellationToken ct) => _getProviderIcon.Handle(body, ct).AsTask();
}
