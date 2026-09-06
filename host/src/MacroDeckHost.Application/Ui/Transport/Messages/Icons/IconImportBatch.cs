namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class IconImportBatch
{
	public string Id { get; set; } = string.Empty;
	public string PackId { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public string? SourceName { get; set; }
	public int? Total { get; set; }
	public int Processed { get; set; }
	public int Failed { get; set; }
	public string? Error { get; set; }
}
