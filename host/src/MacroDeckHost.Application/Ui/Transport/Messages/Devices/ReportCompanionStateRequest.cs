namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class ReportCompanionStateRequest
{
	public int? BatteryLevelPercent { get; set; }

	public bool Charging { get; set; }

	public string? Orientation { get; set; }

	public int? ScreenBrightnessPercent { get; set; }

	public string? Model { get; set; }

	public string? Platform { get; set; }

	public string? AppVersion { get; set; }
}
