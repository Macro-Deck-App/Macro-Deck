namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigFlowAuthorizedNotification
{
	public string FlowId { get; set; } = string.Empty;

	public bool Success { get; set; }

	public string? Error { get; set; }
}
