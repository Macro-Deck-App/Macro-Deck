using System.Text.Json.Serialization;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionOutcomeStatus
{
	// Failed is deliberately the default, for the same reason as ActionExecutionStatus: an unset
	// status must not read as "this step worked". Serialized by name, so the order is not wire state.
	Failed,
	Succeeded,
	Accepted,
	Skipped
}

public class ActionOutcomeDto
{
	public string BlockId { get; set; } = string.Empty;
	public string? Label { get; set; }
	public string? IntegrationId { get; set; }
	public string? ActionId { get; set; }
	public ActionOutcomeStatus Status { get; set; }
	public string? ErrorCode { get; set; }
	public LocalizedText ErrorMessage { get; set; }
	public long DurationMs { get; set; }
}
