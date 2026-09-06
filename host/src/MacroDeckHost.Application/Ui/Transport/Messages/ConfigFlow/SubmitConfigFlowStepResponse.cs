using System.Text.Json.Serialization;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfigFlowOutcomeKind
{
	Step,
	Error,
	Complete,
	External
}

public class SubmitConfigFlowStepResponse
{
	public ConfigFlowOutcomeKind Kind { get; set; }

	public ConfigFlowStepDto? Step { get; set; }

	public LocalizedText Message { get; set; }

	public Dictionary<string, LocalizedText>? FieldErrors { get; set; }

	public string? EntryId { get; set; }

	public string? ExternalUrl { get; set; }

	public string? ResumeStepId { get; set; }

	public TransportError? Error { get; set; }
}
