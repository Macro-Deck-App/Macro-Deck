using MacroDeck.Localization;

namespace MacroDeckHost.Application.Actions;

public enum ActionOutcomeStatus
{
	Succeeded,
	Accepted,

	Skipped,
	Failed
}

public sealed record ActionExecutionOutcome(
	string BlockId,
	string? Label,
	string? IntegrationId,
	string? ActionId,
	ActionOutcomeStatus Status,
	string? ErrorCode,
	LocalizedText ErrorMessage,
	long DurationMs);
