using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class SubmitConfigFlowStepRequest
{
	public string FlowId { get; set; } = string.Empty;

	public string StepId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement> Values { get; set; } = new();

	public List<string> ClearedSecretFields { get; set; } = new();
}
