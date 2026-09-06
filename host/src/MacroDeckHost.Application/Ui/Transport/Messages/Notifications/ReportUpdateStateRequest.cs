namespace MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

public class ReportUpdateStateRequest
{
	public string Version { get; set; } = string.Empty;

	public string Phase { get; set; } = string.Empty;

	public string? PublishedAt { get; set; }

	public long? Downloaded { get; set; }

	public long? Total { get; set; }

	public int? Percent { get; set; }

	public string? Error { get; set; }

	public bool CanInstall { get; set; }
}
