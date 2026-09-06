namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class ImportIconPacksResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }

	public IconImportBatch? Batch { get; set; }

	public List<IconPack> Packs { get; set; } = [];
}
