namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class ImportIconsFromPathRequest
{
	public string? PackId { get; set; }

	public List<string>? Paths { get; set; } = [];
}
