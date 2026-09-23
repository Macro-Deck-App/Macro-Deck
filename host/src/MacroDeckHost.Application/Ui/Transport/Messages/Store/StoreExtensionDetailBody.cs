namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreExtensionDetailBody : StoreCatalogItemBody
{
	public string? LongDescription { get; set; }

	public string? Changelog { get; set; }

	public string? Repository { get; set; }

	public string? License { get; set; }

	public List<StoreExtensionLinkBody> AdditionalLinks { get; set; } = [];

	public long DownloadSize { get; set; }

	public List<string> SupportedOperatingSystems { get; set; } = [];

	public List<string> Languages { get; set; } = [];

	// Null when the package declares nothing, which a client must not show as "uses no AI".
	public StoreAiDeclarationBody? Ai { get; set; }

	public List<string> Tags { get; set; } = [];

	public List<StoreScreenshotBody> Screenshots { get; set; } = [];

	public List<StoreVersionHistoryBody> History { get; set; } = [];
}
