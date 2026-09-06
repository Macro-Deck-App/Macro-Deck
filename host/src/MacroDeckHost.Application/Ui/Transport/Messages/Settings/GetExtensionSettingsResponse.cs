namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetExtensionSettingsResponse
{
	public bool StoreEnabled { get; set; }

	public bool CheckForUpdates { get; set; }

	public bool NotifyOnUpdates { get; set; }

	public int RefreshIntervalMinutes { get; set; }
}
