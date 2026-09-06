namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class ImportIconsResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public IconImportBatch? Batch { get; set; }
}
