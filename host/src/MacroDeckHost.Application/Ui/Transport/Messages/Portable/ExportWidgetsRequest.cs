namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ExportWidgetsRequest : ExportArchiveRequest
{
	public string FolderId { get; set; } = string.Empty;

	public List<string> WidgetIds { get; set; } = [];
}
