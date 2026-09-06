namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreVersionHistoryBody
{
	public string Version { get; set; } = string.Empty;

	public DateTimeOffset? ReleasedAt { get; set; }

	public string? Changelog { get; set; }
}
