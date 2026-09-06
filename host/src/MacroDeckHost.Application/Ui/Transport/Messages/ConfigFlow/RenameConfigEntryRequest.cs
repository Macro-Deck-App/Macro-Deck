namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class RenameConfigEntryRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string EntryId { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;
}
