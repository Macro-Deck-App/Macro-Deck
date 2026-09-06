namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class IconsAddedEvent
{
	public string? BatchId { get; set; }
	public string PackId { get; set; } = string.Empty;
	public List<Icon> Icons { get; set; } = [];
}
