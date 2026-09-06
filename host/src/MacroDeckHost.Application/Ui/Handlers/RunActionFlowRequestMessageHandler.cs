using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Enums;
using Serilog;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class RunActionFlowRequestMessageHandler
	: IUiTransportMessageHandler<RunActionFlowRequest, RunActionFlowResponse>
{
	private static readonly TimeSpan _defaultExecutionTimeout = TimeSpan.FromMinutes(2);

	private readonly IFlowExecutor _flowExecutor;
	private readonly ILogger _logger;
	private readonly TimeSpan _executionTimeout;

	public RunActionFlowRequestMessageHandler(
		IFlowExecutor flowExecutor,
		ILogger logger,
		TimeSpan? executionTimeout = null)
	{
		_flowExecutor = flowExecutor;
		_logger = logger.ForContext<RunActionFlowRequestMessageHandler>();
		_executionTimeout = executionTimeout ?? _defaultExecutionTimeout;
	}

	public async ValueTask<RunActionFlowResponse> Handle(
		RunActionFlowRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.TriggerId))
		{
			return Fail("VALIDATION_ERROR", AppStrings.Errors.Actions.TriggerIdRequired());
		}

		if (string.IsNullOrWhiteSpace(request.Flows))
		{
			return Fail("VALIDATION_ERROR", AppStrings.Errors.Actions.FlowsRequired());
		}

		var scope = VariableDtoMapper.ScopeFromWire(request.Scope);
		if (scope is null && !string.IsNullOrEmpty(request.Scope))
		{
			return Fail("VALIDATION_ERROR", AppStrings.Errors.Variables.UnknownScope());
		}

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(_executionTimeout);

		FlowExecutionResult result;
		try
		{
			result = await _flowExecutor.ExecuteAsync(new FlowExecutionRequest
				{
					FlowsSource = WidgetFlowsJson.ToSource(request.Flows),
					Trigger = TriggerSelector.ById(request.TriggerId),
					Scope = scope ?? VariableScope.Global,
					ScopeRefId = request.ScopeRefId,
					OwnerWidgetId = OwnerWidgetIdOf(scope, request.ScopeRefId),
					OriginClientId = request.ClientId
				},
				timeoutSource.Token);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Test run of flow {TriggerId} failed", request.TriggerId);
			var (code, message) = ActionErrorSanitizer.Sanitize(ex);
			return Fail(code, message);
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (result.Status == FlowExecutionStatus.Cancelled && timeoutSource.IsCancellationRequested)
		{
			_logger.Warning("Test run of flow {TriggerId} timed out after {Timeout}",
				request.TriggerId,
				_executionTimeout);
			return ToResponse(result,
				ActionExecutionStatus.Cancelled,
				"TIMEOUT",
				AppStrings.Errors.Actions.FlowStillRunning(timeout: DescribeTimeout(_executionTimeout)));
		}

		if (result.ErrorCode == ActionExecutionErrorCodes.HostLocked)
		{
			return ToResponse(result,
				ActionExecutionStatus.Failed,
				ActionExecutionErrorCodes.HostLocked,
				result.ErrorMessage.IsEmpty ? AppStrings.Errors.Common.HostLocked() : result.ErrorMessage);
		}

		if (result.MatchedFlows == 0)
		{
			return ToResponse(result,
				ActionExecutionStatus.Failed,
				"FLOW_NOT_FOUND",
				AppStrings.Errors.Actions.FlowNotFoundByTrigger());
		}

		var dto = ActionExecutionDtoMapper.ToDto(result);
		return new RunActionFlowResponse
		{
			Success = ActionExecutionDtoMapper.IsSuccess(dto.Status),
			DurationMs = dto.DurationMs,
			Error = dto.Error,
			ExecutionId = dto.ExecutionId,
			Status = dto.Status,
			Actions = dto.Actions
		};
	}

	private static RunActionFlowResponse ToResponse(
		FlowExecutionResult result,
		ActionExecutionStatus status,
		string errorCode,
		LocalizedText errorMessage)
	{
		var dto = ActionExecutionDtoMapper.ToDto(result);
		return new RunActionFlowResponse
		{
			Success = false,
			DurationMs = dto.DurationMs,
			Error = new TransportError { Code = errorCode, Message = errorMessage },
			ExecutionId = dto.ExecutionId,
			Status = status,
			Actions = dto.Actions
		};
	}

	private static Guid? OwnerWidgetIdOf(VariableScope? scope, string? scopeRefId)
		=> scope == VariableScope.Widget && Guid.TryParse(scopeRefId, out var widgetId) ? widgetId : null;

	private static string DescribeTimeout(TimeSpan timeout) => timeout < TimeSpan.FromMinutes(1)
		? $"{timeout.TotalSeconds:0} seconds"
		: $"{timeout.TotalMinutes:0} minutes";

	private static RunActionFlowResponse Fail(string code, LocalizedText message)
		=> new()
		{
			Success = false,
			Status = ActionExecutionStatus.Failed,
			Error = new TransportError { Code = code, Message = message }
		};
}
