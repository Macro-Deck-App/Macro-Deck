namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class ImportSingleIconFromPathRequest
{
	public string? PackId { get; set; }

	public string Path { get; set; } = string.Empty;
}
