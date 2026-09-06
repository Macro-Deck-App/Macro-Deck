namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class GetConfigEntriesResponse
{
	public List<ConfigEntryDto> Entries { get; set; } = new();
}
