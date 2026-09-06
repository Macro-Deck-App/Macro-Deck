namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class DeleteConfigEntryRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string EntryId { get; set; } = string.Empty;

	public bool Confirmed { get; set; }
}
