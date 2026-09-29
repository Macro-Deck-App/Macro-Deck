namespace MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

public class ReportInstallationIntegrityRequest
{
	public string Status { get; set; } = string.Empty;

	public string? Reason { get; set; }

	public int Missing { get; set; }

	public int Modified { get; set; }

	public string? InstallKind { get; set; }
}
