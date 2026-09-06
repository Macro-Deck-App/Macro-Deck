namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateExtensionSettingsResponse
{
	public bool StoreEnabled { get; set; }

	public bool CheckForUpdates { get; set; }

	public bool NotifyOnUpdates { get; set; }

	public int RefreshIntervalMinutes { get; set; }
}
