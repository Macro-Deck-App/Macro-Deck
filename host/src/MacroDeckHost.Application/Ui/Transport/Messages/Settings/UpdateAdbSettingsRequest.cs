namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateAdbSettingsRequest
{
	public bool? Enabled { get; set; }

	public string? ExecutablePath { get; set; }

	public bool? UsbConnectionsEnabled { get; set; }

	public string? DefaultDeviceSerial { get; set; }

	public bool? StopServerOnExit { get; set; }

	public bool? AllowPlugins { get; set; }
}
