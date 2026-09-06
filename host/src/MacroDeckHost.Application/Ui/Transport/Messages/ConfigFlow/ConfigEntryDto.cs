namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

public class ConfigEntryDto
{
	public string Id { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public string Status { get; set; } = string.Empty;

	public bool Usable { get; set; }
}
