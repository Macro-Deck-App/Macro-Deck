namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreExtensionDetailBody : StoreCatalogItemBody
{
	public string? LongDescription { get; set; }

	public string? Changelog { get; set; }

	public string? Repository { get; set; }

	public string? License { get; set; }

	public long DownloadSize { get; set; }

	public List<string> SupportedOperatingSystems { get; set; } = [];

	public List<string> Languages { get; set; } = [];

	public List<StoreScreenshotBody> Screenshots { get; set; } = [];

	public List<StoreVersionHistoryBody> History { get; set; } = [];
}
