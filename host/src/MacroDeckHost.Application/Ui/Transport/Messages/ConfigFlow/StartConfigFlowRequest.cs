namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class StartConfigFlowRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string? Title { get; set; }

	public string? EntryId { get; set; }
}
