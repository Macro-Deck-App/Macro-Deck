namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetStateChangedNotification
{
	public string WidgetId { get; set; } = string.Empty;
	public string FolderId { get; set; } = string.Empty;
	public string? Data { get; set; }
}
