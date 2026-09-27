namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreVersionHistoryBody
{
	public const string UnsupportedPlatform = "UnsupportedPlatform";
	public const string Unavailable = "Unavailable";
	public const string Withdrawn = "Withdrawn";

	public string Version { get; set; } = string.Empty;

	public DateTimeOffset? ReleasedAt { get; set; }

	public string? Changelog { get; set; }

	public long? Size { get; set; }

	public bool Installable { get; set; }

	public string? UnavailableReason { get; set; }
}
