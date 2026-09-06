using MacroDeck.Localization;
using MacroDeckHost.Application.Actions;
using EngineOutcomeStatus = MacroDeckHost.Application.Actions.ActionOutcomeStatus;
using EngineStatus = MacroDeckHost.Application.Actions.FlowExecutionStatus;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public static class ActionExecutionDtoMapper
{
	public static ActionExecutionResultDto ToDto(FlowExecutionResult result) => new()
	{
		ExecutionId = result.ExecutionId.ToString(),
		Status = MapStatus(result.Status),
		DurationMs = result.DurationMs,
		Actions = result.Actions.Select(MapOutcome).ToList(),
		Error = ToError(result.ErrorCode, result.ErrorMessage)
	};

	public static ActionExecutionStatusEvent ToStatusEvent(
		FlowExecutionResult result,
		string? widgetId,
		string? triggerType) => new()
	{
		ExecutionId = result.ExecutionId.ToString(),
		Status = MapStatus(result.Status),
		DurationMs = result.DurationMs,
		WidgetId = widgetId,
		TriggerType = triggerType,
		Error = ToError(result.ErrorCode, result.ErrorMessage),
		Actions = result.Actions.Select(MapOutcome).ToList()
	};

	public static bool IsSuccess(ActionExecutionStatus status)
		=> status is ActionExecutionStatus.Succeeded or ActionExecutionStatus.Accepted;

	private static TransportError? ToError(string? code, LocalizedText message)
		=> code is null ? null : new TransportError { Code = code, Message = message };

	private static ActionOutcomeDto MapOutcome(ActionExecutionOutcome outcome) => new()
	{
		BlockId = outcome.BlockId,
		Label = outcome.Label,
		IntegrationId = outcome.IntegrationId,
		ActionId = outcome.ActionId,
		Status = MapOutcomeStatus(outcome.Status),
		ErrorCode = outcome.ErrorCode,
		ErrorMessage = outcome.ErrorMessage,
		DurationMs = outcome.DurationMs
	};

	private static ActionExecutionStatus MapStatus(EngineStatus status) => status switch
	{
		EngineStatus.Succeeded => ActionExecutionStatus.Succeeded,
		EngineStatus.PartiallyFailed => ActionExecutionStatus.PartiallyFailed,
		EngineStatus.Failed => ActionExecutionStatus.Failed,
		EngineStatus.Cancelled => ActionExecutionStatus.Cancelled,
		_ => ActionExecutionStatus.Failed
	};

	private static ActionOutcomeStatus MapOutcomeStatus(EngineOutcomeStatus status) => status switch
	{
		EngineOutcomeStatus.Succeeded => ActionOutcomeStatus.Succeeded,
		EngineOutcomeStatus.Accepted => ActionOutcomeStatus.Accepted,
		EngineOutcomeStatus.Skipped => ActionOutcomeStatus.Skipped,
		EngineOutcomeStatus.Failed => ActionOutcomeStatus.Failed,
		_ => ActionOutcomeStatus.Failed
	};
}
