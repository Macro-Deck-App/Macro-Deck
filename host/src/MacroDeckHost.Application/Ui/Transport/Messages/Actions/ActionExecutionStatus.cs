using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionExecutionStatus
{
	// Failed is deliberately the default value: a DTO that reached the wire without anyone setting a
	// status is a bug, and it should read as a failure rather than quietly claiming the run worked.
	// The values are serialized by name, so the order carries no wire meaning.
	Failed,
	Accepted,
	Succeeded,
	PartiallyFailed,
	Cancelled
}
